namespace KnockBox.Services.State.Users
{
    public record class UserNameChangedArgs(string PreviousName, string NewName);

    /// <summary>
    /// A user.
    /// </summary>
    /// <param name="UserName"></param>
    /// <param name="UserId"></param>
    public class User(string name, string id)
    {
        /// <summary>
        /// The name of the user. Capped to 12 characters.
        /// </summary>
        public string Name 
        { 
            get => field;
            set
            {
                // Limit to 12 characters
                value = value.Trim();
                if (value.Length > 12) value = value[..12];
                if (field == value) return;

                string previousName = field;
                field = value;
                
                try
                {
                    NameChanged?.Invoke(new(previousName, value));
                }
                catch { } // Ignore errors
            }
        } = name;

        /// <summary>
        /// The unique id of the user.
        /// </summary>
        public string Id => id;

        /// <summary>
        /// Invoked when the user name has changed.
        /// </summary>
        public event Action<UserNameChangedArgs>? NameChanged;
    }

    public interface IUserService
    {
        /// <summary>
        /// The user information for this circuit. Null if not initialized.
        /// </summary>
        User? CurrentUser { get; }

        /// <summary>
        /// Initializes the current user.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task InitializeCurrentUserAsync(CancellationToken ct = default);
    }
}
