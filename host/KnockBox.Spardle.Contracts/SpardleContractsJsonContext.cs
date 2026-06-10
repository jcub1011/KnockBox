using System.Text.Json.Serialization;

namespace KnockBox.Spardle.Contracts;

/// <summary>
/// Source-generated JSON metadata for every Spardle wire DTO, so the projected
/// view and the command payloads survive IL trimming in the WASM client. Enums
/// are written as strings (<c>UseStringEnumConverter</c>) to match the server's
/// reflection-based hub serializer. The view's nested types (board/outcome/
/// standing records, GuessResult, the enums) are pulled in transitively.
/// </summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(SpardleView))]
[JsonSerializable(typeof(SpardleSettingsView))]
[JsonSerializable(typeof(StartPayload))]
[JsonSerializable(typeof(SubmitGuessPayload))]
[JsonSerializable(typeof(KickPlayerPayload))]
public partial class SpardleContractsJsonContext : JsonSerializerContext;
