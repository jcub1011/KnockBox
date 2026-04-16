namespace KnockBox.Core.Services.State.Games.Shared.Interfaces;

/// <summary>
/// Marker interface for game states that expose a mutable config record of
/// tunable parameters (round count, timer durations, deck composition, etc.)
/// that hosts can adjust in the lobby before starting the game.
/// </summary>
/// <typeparam name="TConfig">
/// The config type — typically a record or small POCO with a public
/// parameterless constructor so callers can replace it wholesale.
/// </typeparam>
public interface IConfigurableGameState<TConfig> where TConfig : class, new()
{
    /// <summary>
    /// The current configuration for this lobby. Usually edited via an
    /// <c>Execute</c>-wrapped setter so the change is serialized and
    /// subscribers are notified.
    /// </summary>
    TConfig Config { get; set; }
}
