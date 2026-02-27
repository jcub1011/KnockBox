namespace KnockBox.Services.State.Users
{
    public class UserService : IUserService
    {
        public User? CurrentUser { get; private set; }

        public Task InitializeCurrentUserAsync(CancellationToken ct = default)
        {
            CurrentUser = new("Not Set", Guid.CreateVersion7().ToString());
            return Task.CompletedTask;
        }
    }
}
