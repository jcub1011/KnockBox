using KnockBox.Extensions.Returns;
using KnockBox.Services.Logic.Games.Shared;
using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Services.State.Games.Shared
{
    public interface IGameSessionService
    {
        /// <summary>
        /// Gets the current session the user is part of. Null when the user is not in a session.
        /// </summary>
        /// <param name="currentSession"></param>
        /// <returns></returns>
        bool TryGetCurrentSession([NotNullWhen(true)] out UserRegistration? currentSession);

        /// <summary>
        /// Sets the current session the user is part of. Must leave the current session first if applicable.
        /// Automatically navigates the user to the game session.
        /// </summary>
        /// <returns></returns>
        Result SetCurrentSession(UserRegistration session);

        /// <summary>
        /// Leaves the current session the user is a part of. Automatically navigates to the home page.
        /// </summary>
        /// <returns></returns>
        Result LeaveCurrentSession(bool navigateHome = true);
    }
}
