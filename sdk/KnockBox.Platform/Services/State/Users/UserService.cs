namespace KnockBox.Services.State.Users
{
    using KnockBox.Core.Primitives.Returns;
    using KnockBox.Core.Services.State.Shared;
    using KnockBox.Core.Services.State.Users;
    using KnockBox.Platform.ClientStorage;

    public class UserService(ILocalStorageService localStorageService, ISessionTokenProvider sessionTokenProvider, ILogger<UserService> logger) : IUserService, IDisposable
    {
        const int MAX_SESSION_TOKEN_RETRIEVALS = 5;

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

                int remainingAttempts = MAX_SESSION_TOKEN_RETRIEVALS;
                ValueResult<SessionToken> tokenResult;
                do
                {
                    tokenResult = await sessionTokenProvider.GetSessionTokenAsync(ct);
                    if (tokenResult.TryGetFailure(out var error))
                    {
                        logger.LogError("{error}\nError getting session token. Reattempting token retrieval.", error);
                        await Task.Delay(100, ct); // Space attempts apart
                    }
                }
                while (--remainingAttempts > 0);

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

        private async void OnNameChanged(UserNameChangedArgs args)
        {
            try
            {
                await localStorageService.SetAsync("user", "name", args.NewName);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error saving user name.");
            }
        }

        public void Dispose()
        {
            CurrentUser?.NameChanged -= OnNameChanged;
        }
    }
}
