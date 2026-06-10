using System.Text.Json.Serialization;

namespace KnockBox.Operator.Contracts;

/// <summary>
/// Source-generated JSON context so the contract DTOs survive IL trimming in the WASM
/// client without reflection roots. <c>UseStringEnumConverter</c> matches the server's
/// wire format (the host's <c>GameViewCoordinator</c> writes enums as strings) for the
/// projected view and every command payload. <see cref="OperatorSettingsView"/> doubles
/// as the <c>update-settings</c> payload.
/// </summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(OperatorView))]
[JsonSerializable(typeof(OperatorSettingsView))]
[JsonSerializable(typeof(StartPayload))]
[JsonSerializable(typeof(SetupChoicePayload))]
[JsonSerializable(typeof(PlayCardsPayload))]
[JsonSerializable(typeof(PlayReactionPayload))]
[JsonSerializable(typeof(RedirectPayload))]
[JsonSerializable(typeof(KickPayload))]
public partial class OperatorContractsJsonContext : JsonSerializerContext;
