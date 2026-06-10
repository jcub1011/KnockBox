using System.Text.Json.Serialization;

namespace KnockBox.LinkedList.Contracts
{
    /// <summary>
    /// Source-generated JSON metadata for every Linked List wire DTO, so the projected view and the
    /// command payloads survive IL trimming in the WASM client. Enums are written as strings
    /// (<c>UseStringEnumConverter</c>) to match the server's reflection-based hub serializer.
    /// The view's nested gameplay types (the chain/standing/superlative records, the phase/scoring
    /// enums) are pulled in transitively.
    /// </summary>
    [JsonSourceGenerationOptions(UseStringEnumConverter = true)]
    [JsonSerializable(typeof(LinkedListView))]
    [JsonSerializable(typeof(LinkedListSettingsView))]
    [JsonSerializable(typeof(StartPayload))]
    [JsonSerializable(typeof(SubmitPairPayload))]
    [JsonSerializable(typeof(KickPlayerPayload))]
    public partial class LinkedListContractsJsonContext : JsonSerializerContext;
}
