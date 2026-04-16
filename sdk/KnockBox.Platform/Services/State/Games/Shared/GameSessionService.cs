using KnockBox.Core.Extensions.Returns;
using KnockBox.Core.Services.Logic.Games.Shared;
using KnockBox.Core.Services.Navigation;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Shared;
using KnockBox.Core.Services.State.Users;
using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Services.State.Games.Shared
{
    /// <summary>
    /// Scoped (per-circuit) implementation of <see cref="IGameSessionService"/>.
    /// <para>
    /// Acts as a thin proxy: all persistent session state lives in a
    /// <see cref="GameSessionState"/> instance that is cached by
    /// <see cref="ISessionServiceProvider"/> under the current user's id, so the state
    /// survives Blazor circuit breaks (temporary disconnects or page refreshes) for up to
    /// the provider's configured disposal grace period (default 1 minute).
    /// </para>
    /// <para>
    /// Navigation remains here because <see cref="INavigationService"/> is circuit-scoped and
    /// must not be captured inside the long-lived <see cref="GameSessionState"/>.
    /// </para>
    /// </summary>
    public class GameSessionService(
        ISessionServiceProvider sessionServiceProvider,
        INavigationService navigationService,
        IUserService userService,
        ILogger<GameSessionService> logger) : IGameSessionService, IDisposable
    {
        private readonly Lock _lock = new();
        private IDisposable? _lifecycleToken;
        private GameSessionState? _sessionState;
        private string? _currentUserId;

        /// <summary>
        /// Lazily resolves the <see cref="GameSessionState"/> cached for the current user
        /// from <see cref="ISessionServiceProvider"/>. Returns <see langword="null"/> when
        /// the user identity has not yet been initialized.
        /// </summary>
        private GameSessionState? GetSessionState()
        {
            var user = userService.CurrentUser;
            if (user is null) return null;

            lock (_lock)
            {
                if (_currentUserId != user.Id)
                {
                    _lifecycleToken?.Dispose();
                    _lifecycleToken = null;
                    _sessionState = null;
                    _currentUserId = user.Id;
                }

                if (_sessionState is null)
                {
                    var result = sessionServiceProvider.GetService<GameSessionState>(
                        new SessionToken(user.Id));
                    if (result.TryGetSuccess(out var registration))
                    {
                        _sessionState = registration.Service;
                        _lifecycleToken = registration.LifecycleToken;
                    }
                }

                return _sessionState;
            }
        }

        public bool TryGetCurrentSession([NotNullWhen(true)] out UserRegistration? currentSession)
        {
            var state = GetSessionState();
            if (state is null)
            {
                currentSession = null;
                return false;
            }
            return state.TryGetCurrentSession(out currentSession);
        }

        public Result LeaveCurrentSession(bool navigateHome = true)
        {
            var previousSession = GetSessionState()?.TakeCurrentSession();
            previousSession?.Dispose();

            logger.LogInformation(
                "User [{userId}] left session [{sessionId}].",
                userService.CurrentUser?.Id ?? "Unknown",
                previousSession?.LobbyRegistration.Uri);

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

            var state = GetSessionState();
            if (state is null)
                return Result.FromError("Unable to join session.", "User identity not yet initialized.");

            if (!state.TrySetCurrentSession(session))
                return Result.FromError("Leave the current session before setting a new one.");

            logger.LogInformation(
                "User [{userId}] entered session [{sessionId}].",
                session.User.Id ?? "Unknown",
                session.LobbyRegistration.Uri);

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

        public void Dispose()
        {
            lock (_lock)
            {
                _lifecycleToken?.Dispose();
                _lifecycleToken = null;
                _sessionState = null;
            }
            GC.SuppressFinalize(this);
        }
    }
}
