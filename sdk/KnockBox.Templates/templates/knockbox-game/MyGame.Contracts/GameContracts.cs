// -----------------------------------------------------------------------------
// Shared contracts for MyGame — the typed boundary between the browser UI and the
// server engine.
//
//   - View DTO: what the server PROJECTS to each player. Add only fields the
//     recipient is allowed to see; the server's projector (default-deny) decides
//     what crosses the wire, so secrets never reach the wrong client.
//   - Commands: the names the client sends to the server engine over the hub.
//
// Both the server plugin and the .Client UI reference this assembly, so they agree
// on the exact shape. Keep it free of any KnockBox.* dependency.
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace MyGame.Contracts;

/// <summary>
/// Per-player projected view of a MyGame lobby. Sent server → browser over the hub.
/// </summary>
public sealed record GameView(
    bool IsJoinable,
    string HostName,
    IReadOnlyList<string> PlayerNames);

/// <summary>Command names the client sends to the server engine via the hub.</summary>
public static class GameCommands
{
    public const string Start = "start";
}

/// <summary>
/// Source-generated JSON context so <see cref="GameView"/> survives IL trimming in
/// the WASM client without reflection roots. Pass <c>GameContractsJsonContext.Default</c>
/// to a <c>SourceGenProjectionDeserializer&lt;GameView&gt;</c> for the trim-safe path.
/// </summary>
[JsonSerializable(typeof(GameView))]
public partial class GameContractsJsonContext : JsonSerializerContext;
