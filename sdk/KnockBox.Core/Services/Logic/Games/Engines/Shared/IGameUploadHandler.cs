using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Services.State.Users;

namespace KnockBox.Core.Services.Logic.Games.Engines.Shared
{
    /// <summary>
    /// Optional opt-in contract for game engines that accept <b>file uploads</b>
    /// from their WASM client. It is the focused, ergonomic counterpart to
    /// <see cref="IGameCommandHandler"/> (typed commands in) and
    /// <c>IGameEngineHttpHandler</c> (raw plugin HTTP): the platform's
    /// <c>POST /api/games/upload</c> endpoint resolves the caller from the signed
    /// session token, resolves the room, enforces the configured size cap, and
    /// then hands the engine a ready-to-read <see cref="Stream"/> — so a plugin
    /// never re-implements auth, routing, or size plumbing for an upload.
    /// <para>
    /// Why an upload can't ride the hub: a migrated game's UI talks to
    /// <c>GameHub</c> (no Blazor circuit), whose <c>MaximumReceiveMessageSize</c>
    /// caps a single message well below a multi-hundred-KB file. The HTTP endpoint
    /// streams the body instead.
    /// </para>
    /// <para>
    /// Host-identity authorization (<c>caller.Id == state.Host.Id</c>) is the
    /// handler's responsibility, exactly like <see cref="IGameCommandHandler"/>.
    /// Mutating <paramref name="state"/> via <c>state.Execute*</c> triggers the
    /// per-lobby projection fan-out automatically, so every connection re-projects
    /// after a successful upload — no extra wiring.
    /// </para>
    /// </summary>
    public interface IGameUploadHandler
    {
        /// <summary>
        /// Handles an uploaded file for <paramref name="caller"/> against
        /// <paramref name="state"/>.
        /// </summary>
        /// <param name="caller">Resolved from the session token (a fresh instance per request — compare by <c>Id</c>).</param>
        /// <param name="state">The resolved game state for the room. Mutate only through <c>state.Execute*</c>.</param>
        /// <param name="uploadKind">A plugin-defined discriminator (e.g. <c>"word-pool"</c>) so one game can accept several upload kinds.</param>
        /// <param name="fileName">The client-supplied file name (advisory only — never trust it as a path).</param>
        /// <param name="content">The uploaded bytes, ready to read incrementally. Do NOT assume it fits in memory.</param>
        /// <param name="ct">Cancellation tied to the request lifetime.</param>
        ValueTask<Result> HandleUploadAsync(
            User caller,
            AbstractGameState state,
            string uploadKind,
            string fileName,
            Stream content,
            CancellationToken ct = default);
    }

    /// <summary>
    /// Optional ergonomic base for games whose upload kinds are an <c>enum</c>:
    /// it parses the wire <c>uploadKind</c> string into <typeparamref name="TKind"/>
    /// (case-insensitively) and dispatches to the typed overload, failing with a
    /// clear message before the typed handler runs if the kind is unrecognized.
    /// Authors who prefer non-enum identifiers (int, etc.) implement
    /// <see cref="IGameUploadHandler"/> directly — the host only ever sees the
    /// string interface, so its dispatch stays type-agnostic.
    /// </summary>
    /// <typeparam name="TKind">The plugin's upload-kind enum.</typeparam>
    public abstract class GameUploadHandlerBase<TKind> : IGameUploadHandler
        where TKind : struct, Enum
    {
        /// <inheritdoc />
        public ValueTask<Result> HandleUploadAsync(
            User caller,
            AbstractGameState state,
            string uploadKind,
            string fileName,
            Stream content,
            CancellationToken ct = default)
        {
            if (!Enum.TryParse<TKind>(uploadKind, ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
                return ValueTask.FromResult(Result.FromError($"Unknown upload kind [{uploadKind}]."));

            return HandleUploadAsync(caller, state, kind, fileName, content, ct);
        }

        /// <summary>Typed counterpart invoked once <paramref name="kind"/> has parsed.</summary>
        protected abstract ValueTask<Result> HandleUploadAsync(
            User caller,
            AbstractGameState state,
            TKind kind,
            string fileName,
            Stream content,
            CancellationToken ct);
    }
}
