using System.Text.Json.Serialization;

namespace KnockBox.Tracery.Contracts
{
    /// <summary>
    /// Source-generated JSON metadata for every Tracery wire DTO, so the projected view and the
    /// command payloads survive IL trimming in the WASM client. Enums are written as strings
    /// (<c>UseStringEnumConverter</c>) to match the server's reflection-based hub serializer.
    /// The view's nested gameplay types (Grid, RevealData, RoundResult, the mode/phase enums) are
    /// pulled in transitively.
    /// </summary>
    [JsonSourceGenerationOptions(UseStringEnumConverter = true)]
    [JsonSerializable(typeof(TraceryView))]
    [JsonSerializable(typeof(TracerySettingsView))]
    [JsonSerializable(typeof(StartPayload))]
    [JsonSerializable(typeof(SubmitTracePayload))]
    [JsonSerializable(typeof(KickPlayerPayload))]
    public partial class TraceryContractsJsonContext : JsonSerializerContext;
}
