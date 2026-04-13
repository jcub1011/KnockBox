using KnockBox.Core.Services.Logic.Games.Shared;
using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Core.Services.State.Games.Shared
{
    /// <summary>
    /// Transient, user-ID-backed state holder for a single user's active game session.
    /// <para>
    /// Registered as Transient in the DI container so that <see cref="ISessionServiceProvider"/>
    /// can cache exactly one instance per user identifier, surviving Blazor circuit breaks.
    /// When <see cref="ISessionServiceProvider"/> disposes this instance (after the
    /// post-disconnect grace period), <see cref="Dispose"/> automatically removes the user
    /// from the game state so the lobby slot is freed without requiring an active circuit.
    /// </para>
    /// </summary>
    public sealed class GameSessionState : IDisposable
    {
        private UserRegistration? _currentSession;

        /// <inheritdoc cref="IGameSessionService.TryGetCurrentSession"/>
        public bool TryGetCurrentSession([NotNullWhen(true)] out UserRegistration? currentSession)
        {
            currentSession = _currentSession;
            return _currentSession is not null;
        }

        /// <summary>
        /// Atomically sets the session. Returns <see langword="true"/> when the slot was empty and
        /// the session was stored; <see langword="false"/> when a session was already present.
        /// </summary>
        public bool TrySetCurrentSession(UserRegistration session)
        {
            var previousSession = Interlocked.CompareExchange(ref _currentSession, session, null);
            return previousSession is null;
        }

        /// <summary>
        /// Atomically clears the current session and returns it, or <see langword="null"/> if
        /// there was no active session. The caller is responsible for disposing the returned
        /// registration to remove the user from the game state.
        /// </summary>
        public UserRegistration? TakeCurrentSession()
        {
            return Interlocked.Exchange(ref _currentSession, null);
        }

        /// <summary>
        /// Removes the user from the game state when <see cref="ISessionServiceProvider"/>
        /// disposes this instance after the post-disconnect grace period expires.
        /// </summary>
        public void Dispose()
        {
            TakeCurrentSession()?.Dispose();
        }
    }
}
