using KnockBox.Extensions.Events;

namespace KnockBox.Services.State.Games.Lobbies
{
    /// <summary>
    /// The state object for a game.
    /// </summary>
    /// <param name="host"></param>
    /// <param name="logger"></param>
    public abstract class AbstractGameState(UserRegistration host, ILogger logger) : IDisposable
    {
        /// <summary>
        /// The lock for managing players in this game state.
        /// </summary>
        protected readonly Lock PlayerLock = new();
        protected readonly ILogger Logger = logger;
        protected readonly UserRegistration Host = host;

        private readonly Dictionary<Guid, UserRegistration> _players = [];

        /// <summary>
        /// The event manager for this instance of the game state.
        /// </summary>
        public ITypedThreadSafeEventManager EventManager { get; private set; } = new TypedThreadSafeEventManager(logger);

        /// <summary>
        /// The max count of players in the game.
        /// </summary>
        public abstract int PlayerCapacity { get; }

        /// <summary>
        /// Gets the host of the game.
        /// </summary>
        /// <returns></returns>
        public UserRegistration GetHost() => Host;

        /// <summary>
        /// The current roster of players in the game. Does not include the host.
        /// </summary>
        /// <returns></returns>
        public IReadOnlyList<UserRegistration> GetPlayers()
        {
            lock (PlayerLock)
            {
                return [.. _players.Values];
            }
        }

        /// <summary>
        /// Attempts to add the player to the roster of players.
        /// </summary>
        /// <param name="user"></param>
        /// <returns>True if the player was added.</returns>
        public bool TryAddPlayer(UserRegistration user)
        {
            lock (PlayerLock)
            {
                return _players.TryAdd(user.Id, user);
            }
        }

        /// <summary>
        /// Removes the player from the roster of players.
        /// </summary>
        /// <param name="user"></param>
        /// <returns>True if the player was in the roster.</returns>
        public bool RemovePlayer(UserRegistration user)
        {
            lock (PlayerLock)
            {
                return _players.Remove(user.Id);
            }
        }

        public abstract void Dispose();
    }
}
