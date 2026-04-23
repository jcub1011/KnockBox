namespace KnockBox.Services.State.Users
{
    using KnockBox.Core.Primitives.Returns;
    using KnockBox.Core.Services.State.Shared;
    using KnockBox.Core.Services.State.Users;
    using KnockBox.Platform.ClientStorage;

    public class UserService(ILocalStorageService localStorageService, ISessionTokenProvider sessionTokenProvider, ILogger<UserService> logger) : IUserService, IDisposable
    {
        const int MAX_SESSION_TOKEN_RETRIEVALS = 5;
        const int MAX_NAME_LENGTH = 12;

        private readonly CancellationTokenSource _disposeCts = new();
        private int _disposed;

        public User? CurrentUser { get; private set; }

        public event Action? UserInitialized;
        public event Action<UserNameChangedArgs>? UserNameChanged;

        public async Task InitializeCurrentUserAsync(CancellationToken ct = default)
        {
            string name = "Not Set";
            string id = Guid.CreateVersion7().ToString();
            try
            {
                var storedName = await localStorageService.GetAsync<string>("user", "name", ct);
                if (!string.IsNullOrWhiteSpace(storedName))
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
                    id = token.Token;
                    logger.LogDebug("Initialized user id to [{id}].", id);
                }
                else
                {
                    logger.LogError("Unable to get player session token. Using fallback ID of {id}.", id);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error initializing current user service.");
            }

            CurrentUser = UserFactory.Create(name, id);
            UserInitialized?.Invoke();
        }

        public async Task ResetIdentityAsync(CancellationToken ct = default)
        {
            try
            {
                await sessionTokenProvider.ProvisionNewTokenAsync(ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error resetting user identity.");
            }

            await InitializeCurrentUserAsync(ct);
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
            try
            {
                await localStorageService.SetAsync("user", "name", name, ct);
            }
            catch (OperationCanceledException) { /* Service disposed — drop silently. */ }
            catch (ObjectDisposedException) { /* Service disposed — drop silently. */ }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error saving user name.");
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
