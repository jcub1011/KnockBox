namespace KnockBox.Spardle.Models;

// Moved into the shared Contracts assembly (keeping the KnockBox.Spardle.Models
// namespace) so the projected view's Phase field round-trips to the WASM client.
public enum GamePhase
{
    Lobby,
    RoundIntro,
    Playing,
    RoundResults,
    GameOver
}
