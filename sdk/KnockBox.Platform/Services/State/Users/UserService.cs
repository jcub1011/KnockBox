namespace KnockBox.Services.State.Users
{
    using KnockBox.Core.Primitives.Returns;
    using KnockBox.Core.Services.State.Shared;
    using KnockBox.Core.Services.State.Users;
    using KnockBox.Platform.ClientStorage;

    public class UserService(ILocalStorageService localStorageService, ISessionTokenProvider sessionTokenProvider, ILogger<UserService> logger) : IUserService, IDisposable
    {
        const int MAX_SESSION_TOKEN_RETRIEVALS = 5;

        private readonly CancellationTokenSource _disposeCts = new();
        private int _disposed;

        public User? CurrentUser { get; private set; }

        public event Action? UserInitialized;

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

                    logger.LogError("Error getting session token (attempt {attempt}/{max}). Reattempting.", attempt + 1, MAX_SESSION_TOKEN_RETRIEVALS);
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

            // Unsubscribe from the previous user (if re-initializing) before replacing it.
            CurrentUser?.NameChanged -= OnNameChanged;

            CurrentUser = new(name, id);
            CurrentUser.NameChanged += OnNameChanged;
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

        private void OnNameChanged(UserNameChangedArgs args)
        {
            // Schedule the persist as a tracked task so a post-dispose fire is cancelled cleanly.
            _ = SaveNameAsync(args.NewName, _disposeCts.Token);
        }

        private async Task SaveNameAsync(string name, CancellationToken ct)
        {
            try
            {
                await localStorageService.SetAsync("user", "name", name, ct);
            }
            catch (OperationCanceledException) { /* Service disposed — drop silently. */ }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error saving user name.");
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            CurrentUser?.NameChanged -= OnNameChanged;
            _disposeCts.Cancel();
            _disposeCts.Dispose();
        }
    }
}
