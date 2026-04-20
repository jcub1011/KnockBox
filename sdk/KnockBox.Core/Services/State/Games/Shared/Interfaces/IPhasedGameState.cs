namespace KnockBox.Core.Services.State.Games.Shared.Interfaces;

/// <summary>
/// Marker interface for game states that progress through a sequence of typed
/// phases (e.g., <c>Lobby</c> → <c>BuyIn</c> → <c>InProgress</c> → <c>GameOver</c>).
/// Implementations call <c>NotifyStateChanged()</c> after updating
/// <see cref="Phase"/> so Razor pages can re-render when the phase changes.
/// </summary>
/// <typeparam name="TPhase">An <see cref="Enum"/> describing this game's phases.</typeparam>
public interface IPhasedGameState<TPhase> where TPhase : struct, Enum
{
    /// <summary>The current phase of the game.</summary>
    TPhase Phase { get; }

    /// <summary>
    /// Transitions the game to <paramref name="phase"/>. Implementations
    /// typically call this from inside an <c>Execute</c> block so the change
    /// is serialized and subscribers are notified.
    /// </summary>
    void SetPhase(TPhase phase);
}
