using KnockBox.Core.Services.Storage.ClientStorage;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Shared;

namespace KnockBox.Services.State.Shared;

public sealed class SessionTokenProvider(ISessionStorageService sessionStorageService, ILogger<SessionTokenProvider> logger) : ISessionTokenProvider
{
    // Technically does not need to be disposed if "AvailableWaitHandle" is never accessed.
    readonly SemaphoreSlim _semaphore = new(1, 1);
    SessionToken _cachedToken;

    public async ValueTask<ValueResult<SessionToken>> GetSessionTokenAsync(CancellationToken ct = default)
    {
        try
        {
            await _semaphore.WaitAsync(ct);

            try
            {
                // Get token from storage
                if (_cachedToken.HashCode == 0)
                {
                    var getResult = await sessionStorageService.GetAsync<string>(nameof(SessionTokenProvider), "token", ct);
                    if (getResult.IsCanceled) return ValueResult<SessionToken>.FromCancellation();
                    if (getResult.TryGetFailure(out var getError)) return getError;
                    getResult.TryGetSuccess(out var tokenString);

                    if (string.IsNullOrWhiteSpace(tokenString))
                    {
                        tokenString = Guid.CreateVersion7().ToString();
                        var setResult = await sessionStorageService.SetAsync(nameof(SessionTokenProvider), "token", tokenString, ct);
                        if (setResult.IsCanceled) return ValueResult<SessionToken>.FromCancellation();
                        if (setResult.TryGetFailure(out var setError)) return setError;
                    }
                    _cachedToken = new SessionToken(tokenString);

                }

                return _cachedToken;
            }
            finally
            {
                _semaphore.Release();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting session token.");
            return new ResultError("Error retrieving session token.");
        }
    }

    public async ValueTask<ValueResult<SessionToken>> ProvisionNewTokenAsync(CancellationToken ct = default)
    {
        try
        {
            await _semaphore.WaitAsync(ct);

            try
            {
                _cachedToken = new SessionToken(Guid.NewGuid());
                var setResult = await sessionStorageService.SetAsync(nameof(SessionTokenProvider), "token", _cachedToken.Token, ct);
                if (setResult.IsCanceled) return ValueResult<SessionToken>.FromCancellation();
                if (setResult.TryGetFailure(out var setError)) return setError;

                return _cachedToken;
            }
            finally
            {
                _semaphore.Release();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error provisioning session token.");
            return new ResultError("Error provisioning session token.");
        }
    }
}
