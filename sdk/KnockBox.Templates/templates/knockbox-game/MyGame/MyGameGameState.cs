// -----------------------------------------------------------------------------
// Per-room game state.
//
// One MyGameGameState instance exists per lobby. The engine creates it in
// CreateStateAsync, the platform stashes it on the lobby's LobbyRegistration,
// and every Razor page in the lobby reads from it and subscribes to its
// StateChangedEventManager for re-renders.
//
// You add your game-specific fields here. You do NOT mutate them directly from
// Razor pages or controllers — all mutations go through the inherited Execute /
// ExecuteAsync helpers so the room-level lock is held and subscribers are
// notified after the mutation completes.
// -----------------------------------------------------------------------------

using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace MyGame;

/// <summary>
/// Per-room state for a single lobby of this game. Subclasses
/// <see cref="AbstractGameState"/>, which provides thread-safe mutation,
/// a roster, join-lock toggling, the <c>StateChangedEventManager</c>, and
/// lifecycle events.
/// </summary>
/// <remarks>
/// <para><b>Inherited surface you will actually use:</b></para>
/// <list type="bullet">
///   <item><c>Host</c>, <c>Players</c>, <c>KickedPlayers</c> — roster snapshot.</item>
///   <item><c>IsJoinable</c> / <c>UpdateJoinableStatus(bool)</c> — controls whether the lobby accepts new joiners.</item>
///   <item><c>Execute(Action)</c> / <c>ExecuteAsync(Func&lt;Task&gt;)</c> — mutation gate.
///     Acquire the per-state <c>SemaphoreSlim(1,1)</c>, run your lambda, release,
///     then fire <c>StateChangedEventManager</c> after the lock is released.
///     <b>All mutation goes through here.</b></item>
///   <item><c>WithExclusiveRead(Action)</c> / <c>WithExclusiveReadAsync(...)</c> —
///     serialized non-mutating reads (no notification fires afterward).</item>
///   <item><c>StateChangedEventManager</c> — subscribe from Razor to re-render
///     whenever the state mutates. <c>Subscribe(...)</c> returns an
///     <c>IDisposable</c> — you MUST dispose it in the component's <c>Dispose()</c>.</item>
///   <item><c>PlayerUnregistered</c> — fires <b>outside</b> the Execute lock, so
///     your handler can safely call <c>Execute</c> again (e.g., advance the turn
///     on disconnect) without deadlocking.</item>
///   <item><c>OnStateDisposed</c> — fires once when the lobby ends. Pages should
///     unsubscribe and navigate home.</item>
/// </list>
/// <para>
/// Example of adding game-specific fields and mutating them safely:
/// <code>
/// public int Round { get; private set; }
/// public string? LastResult { get; private set; }
///
/// // Called from the engine, never from Razor or external callers directly.
/// internal void AdvanceRound(string result) =&gt; Execute(() =&gt;
/// {
///     Round++;
///     LastResult = result;
/// });
/// </code>
/// </para>
/// </remarks>
public class MyGameGameState(User host, ILogger<MyGameGameState> logger)
    : AbstractGameState(host, logger)
{
    // TODO: Add your game-specific state properties here.
    //       Keep setters private or internal so mutation flows through
    //       Execute / ExecuteAsync on this class (or on the engine).
}
