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
        /// The name of the user.
        /// </summary>
        public string Name 
        { 
            get => field;
            set
            {
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
        /// The user information for this circuit.
        /// </summary>
        User CurrentUser { get; }

        /// <summary>
        /// Initializes the current user.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task InitializeCurrentUserAsync(CancellationToken ct = default);
    }
}
