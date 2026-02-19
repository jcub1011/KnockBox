namespace KnockBox.Services.State.Users
{
    public interface IUserService
    {
        public delegate void UserNameChangedDelegate(string previousName, string newName);

        /// <summary>
        /// The user name of this user.
        /// </summary>
        public string UserName { get; }

        /// <summary>
        /// Invoked when the user name is changed.
        /// </summary>
        public event UserNameChangedDelegate? UserNameChanged;

        /// <summary>
        /// The id of this user. Does not change in the life of this user.
        /// </summary>
        public Guid UserId { get; }

        /// <summary>
        /// Changes the current user name.
        /// </summary>
        /// <param name="userName"></param>
        void SetUserName(string userName);
    }
}
