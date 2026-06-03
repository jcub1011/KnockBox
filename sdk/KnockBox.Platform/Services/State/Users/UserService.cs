namespace KnockBox.Services.State.Users
{
    using KnockBox.Core.Primitives.Returns;
    using KnockBox.Core.Services.State.Shared;
    using KnockBox.Core.Services.State.Users;
    using KnockBox.Core.Services.Storage.ClientStorage;

    public class UserService(ILocalStorageService localStorageService, ISessionTokenProvider sessionTokenProvider, ILogger<UserService> logger) : IUserService, IDisposable
    {
        const int MAX_SESSION_TOKEN_RETRIEVALS = 5;
        const int MAX_NAME_LENGTH = 12;

        private readonly CancellationTokenSource _disposeCts = new();
        private int _disposed;

        public User? CurrentUser { get; private set; }

        public event Action? UserInitialized;
        public event Action<UserNameChangedArgs>? UserNameChanged;

        public async Task<Result> InitializeCurrentUserAsync(CancellationToken ct = default)
        {
            string name = "Not Set";
            Guid id = Guid.CreateVersion7();
            Result result = Result.Success;
            try
            {
                var storedNameResult = await localStorageService.GetAsync<string>("user", "name", ct);
                if (storedNameResult.TryGetSuccess(out var storedName) && !string.IsNullOrWhiteSpace(storedName))
                {
                    name = storedName;
                }

                ValueResult<SessionToken> tokenResult = default;
                for (int attempt = 0; attempt < MAX_SESSION_TOKEN_RETRIEVALS; attempt++)
                {
                    tokenResult = await sessionTokenProvider.GetSessionTokenAsync(ct);
                    if (tokenResult.IsSuccess) break;

                    tokenResult.TryGetFailure(out var failure);
                    logger.LogError("Error getting session token (attempt {attempt}/{max}): {error}. Reattempting.",
                        attempt + 1, MAX_SESSION_TOKEN_RETRIEVALS, failure);
                    if (attempt < MAX_SESSION_TOKEN_RETRIEVALS - 1)
                        await Task.Delay(100, ct);
                }

                if (tokenResult.TryGetSuccess(out var token))
                {
                    id = Guid.TryParse(token.Token, out var parsed) ? parsed : Guid.CreateVersion7();
                    logger.LogDebug("Initialized user id to [{id}].", id);
                }
                else
                {
                    logger.LogError("Unable to get player session token. Using fallback ID of {id}.", id);
                    result = Result.FromError(
                        "Unable to establish a session.",
                        $"Unable to get player session token; using fallback id {id}.");
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error initializing current user service.");
                result = Result.FromError("Error initializing user.", $"Error initializing current user service: {ex}");
            }

            CurrentUser = UserFactory.Create(name, id);
            UserInitialized?.Invoke();
            return result;
        }

        public async Task<Result> ResetIdentityAsync(CancellationToken ct = default)
        {
            var provisionResult = await sessionTokenProvider.ProvisionNewTokenAsync(ct);
            if (provisionResult.TryGetFailure(out var error))
            {
                logger.LogError("Error resetting user identity: {error}", error.InternalMessage);
            }

            return await InitializeCurrentUserAsync(ct);
        }

        public void SetCurrentUserName(string name)
        {
            if (Volatile.Read(ref _disposed) == 1) return;

            var user = CurrentUser;
            if (user is null) return;

            name = name.Trim();
            if (name.Length > MAX_NAME_LENGTH) name = name[..MAX_NAME_LENGTH];

            var previous = user.Name;
            if (string.Equals(previous, name, StringComparison.Ordinal)) return;

            user.Name = name;

            RaiseUserNameChanged(previous, name);

            try
            {
                _ = SaveNameAsync(name, _disposeCts.Token);
            }
            catch (ObjectDisposedException)
            {
                // Service disposed between the check above and here — drop silently.
            }
        }

        private void RaiseUserNameChanged(string previous, string current)
        {
            var handlers = UserNameChanged;
            if (handlers is null) return;

            var args = new UserNameChangedArgs(previous, current);
            // Mirror the Phase 1.4 pattern — one throwing subscriber must not
            // short-circuit the rest of the invocation list.
            foreach (Action<UserNameChangedArgs> handler in handlers.GetInvocationList())
            {
                try { handler(args); }
                catch (Exception ex)
                {
                    logger.LogError(ex, "A UserNameChanged subscriber threw.");
                }
            }
        }

        private async Task SaveNameAsync(string name, CancellationToken ct)
        {
            // Best-effort persist: a failed or canceled write is logged (by the storage
            // service for the internal detail) and otherwise dropped silently.
            var result = await localStorageService.SetAsync("user", "name", name, ct);
            if (result.TryGetFailure(out var error))
            {
                logger.LogError("Error saving user name: {error}", error.InternalMessage);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            _disposeCts.Cancel();
            _disposeCts.Dispose();
        }
    }
}
