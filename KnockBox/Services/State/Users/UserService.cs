namespace KnockBox.Services.State.Users
{
    using KnockBox.Data.Services.ClientStorage;

    public class UserService(ILocalStorageService localStorageService, ISessionStorageService sessionStorageService, ILogger<UserService> logger) : IUserService, IDisposable
    {
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

                var storedId = await sessionStorageService.GetAsync<string>("user", "id", ct);
                if (!string.IsNullOrWhiteSpace(storedId))
                {
                    id = storedId;
                }
                else
                {
                    await sessionStorageService.SetAsync("user", "id", id, ct);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error initializing current user service.");
            }

            // Unsubscribe from the previous user (if re-initializing) before replacing it.
            if (CurrentUser is not null)
                CurrentUser.NameChanged -= OnNameChanged;

            CurrentUser = new(name, id);
            CurrentUser.NameChanged += OnNameChanged;
            UserInitialized?.Invoke();
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
            if (CurrentUser is not null)
                CurrentUser.NameChanged -= OnNameChanged;
        }
    }
}
