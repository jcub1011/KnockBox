using static KnockBox.Services.State.Users.IUserService;

namespace KnockBox.Services.State.Users
{
    public class UserService : IUserService
    {
        public string UserName { get; private set; } = string.Empty;

        public event UserNameChangedDelegate? UserNameChanged;

        public Guid UserId { get; } = Guid.NewGuid();

        public void SetUserName(string userName)
        {
            if (UserName == userName) return;

            string prev = UserName;
            UserName = userName;
            UserNameChanged?.Invoke(prev, userName);
        }
    }
}
