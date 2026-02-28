namespace KnockBox.Services.State.Users
{
    using KnockBox.Data.Services.ClientStorage;

    public class UserService(ILocalStorageService localStorageService, ILogger<UserService> logger) : IUserService
    {
        public User? CurrentUser { get; private set; }

        public async Task InitializeCurrentUserAsync(CancellationToken ct = default)
        {
            string name = "Not Set";
            try
            {
                var storedName = await localStorageService.GetAsync<string>("user", "name", ct);
                if (!string.IsNullOrWhiteSpace(storedName))
                {
                    name = storedName;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error inititalizing current user service.");
            }

            CurrentUser = new(name, Guid.CreateVersion7().ToString());
            CurrentUser.NameChanged += OnNameChanged;
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
    }
}
