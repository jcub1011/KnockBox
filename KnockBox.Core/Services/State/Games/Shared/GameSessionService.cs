using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.Shared;
using KnockBox.Services.Navigation;
using KnockBox.Services.State.Users;
using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Services.State.Games.Shared
{
    public class GameSessionService(
        INavigationService navigationService, 
        IUserService userService,
        ILogger<GameSessionService> logger) : IGameSessionService
    {
        private UserRegistration? _currentSession = null;

        public bool TryGetCurrentSession([NotNullWhen(true)] out UserRegistration? currentSession)
        {
            currentSession = _currentSession;
            return _currentSession is not null;
        }

        public Result LeaveCurrentSession(bool navigateHome = true)
        {
            var previousSession = Interlocked.Exchange(ref _currentSession, null);
            previousSession?.Dispose(); // Leaves the lobby

            logger.LogInformation("User [{userId}] left session [{sessionId}].", userService.CurrentUser?.Id ?? "Unknown", previousSession?.LobbyRegistration.Uri);

            try
            {
                if (navigateHome) navigationService.ToHome();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error navigating to the home page.");
            }

            return Result.Success;
        }

        public Result SetCurrentSession(UserRegistration session)
        {
            if (session is null)
                return Result.FromError("Unable to join session.", "Session cannot be null.");

            var previousSession = Interlocked.CompareExchange(ref _currentSession, session, null);

            if (previousSession is not null)
                return Result.FromError("Leave the current session before setting a new one.");

            logger.LogInformation("User [{userId}] entered session [{sessionId}].", userService.CurrentUser?.Id ?? "Unknown", session.LobbyRegistration.Uri);

            try
            {
                navigationService.ToGame(session.LobbyRegistration);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error navigating to the lobby page.");
            }

            return Result.Success;
        }
    }
}
