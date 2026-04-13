using KnockBox.Data.Services.ClientStorage;
using KnockBox.Extensions.Returns;

namespace KnockBox.Services.State.Shared;

public class SessionTokenProvider(ISessionStorageService sessionStorageService, ILogger<SessionTokenProvider> logger) : ISessionTokenProvider
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
                    string tokenString = await sessionStorageService.GetAsync<string>(nameof(SessionTokenProvider), "token", ct);
                    if (string.IsNullOrWhiteSpace(tokenString))
                    {
                        tokenString = Guid.NewGuid().ToString();
                        await sessionStorageService.SetAsync(nameof(SessionTokenProvider), "token", tokenString, ct);
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
                await sessionStorageService.SetAsync(nameof(SessionTokenProvider), "token", _cachedToken.Token, ct);

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
