using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.Core.Services.Logic.Games.Engines.Shared
{
    /// <summary>
    /// Dispatches a typed-over-the-wire game command to the engine without the
    /// host needing compile-time knowledge of the plugin's command surface. In
    /// the WASM model a client sends <c>(command, payloadJson)</c> over the hub;
    /// the hub resolves the engine (keyed <see cref="AbstractGameEngine"/>) and,
    /// if it implements this interface, dispatches here. The engine maps the
    /// command name to the same engine method a Razor page used to call directly,
    /// mutating through <see cref="AbstractGameState.Execute(Action)"/>.
    /// <para>
    /// Host-identity authorization (<c>caller.Id == state.Host.Id</c>) is the
    /// engine's responsibility per command, reusing the sealed checks already in
    /// <see cref="AbstractGameEngine"/>.
    /// </para>
    /// </summary>
    public interface IGameCommandHandler
    {
        /// <summary>
        /// Handles <paramref name="command"/> for <paramref name="caller"/> against
        /// <paramref name="state"/>. <paramref name="payloadJson"/> is the optional
        /// command argument payload (game-defined shape) or <see langword="null"/>.
        /// </summary>
        ValueTask<Result> HandleCommandAsync(
            User caller,
            AbstractGameState state,
            string command,
            string? payloadJson,
            CancellationToken ct = default);
    }
}
