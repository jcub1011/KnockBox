using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.Navigation;
using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Services.State.Games.Shared
{
    public class GameSessionService(INavigationService navigationService) : IGameSessionService
    {
        private UserRegistration? _currentSession = null;

        public bool TryGetCurrentSession([NotNullWhen(true)] out UserRegistration? currentSession)
        {
            currentSession = _currentSession;
            return _currentSession is not null;
        }

        public Result LeaveCurrentSession()
        {
            var previousSession = Interlocked.Exchange(ref _currentSession, null);
            previousSession?.Dispose(); // Leaves the lobby
            navigationService.ToHome();
            return Result.Success;
        }

        public Result SetCurrentSession(UserRegistration session)
        {
            if (session is null)
                return Result.FromError(new ArgumentNullException(nameof(session), "Session cannot be null."));

            var previousSession = Interlocked.CompareExchange(ref _currentSession, session, null);

            if (previousSession is not null)
                return Result.FromError(new InvalidOperationException("Leave the current session before setting a new one."));

            navigationService.ToGame(session.LobbyRegistration);
            return Result.Success;
        }
    }
}
