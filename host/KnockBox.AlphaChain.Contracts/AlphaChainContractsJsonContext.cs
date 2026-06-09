using System.Text.Json.Serialization;

namespace KnockBox.AlphaChain.Contracts;

/// <summary>
/// Source-generated JSON for the Alpha Chain contracts so the projected view and command
/// payloads serialize trim-safe in the WASM client. <c>UseStringEnumConverter</c> matches the
/// hub's wire format (enums by name), and the client pairs it with case-insensitive property
/// matching when deserializing. The view DTO is registered as the deserialization root for the
/// projection; each command payload is registered for serialization on submit.
/// </summary>
[JsonSourceGenerationOptions(UseStringEnumConverter = true)]
[JsonSerializable(typeof(AlphaChainView))]
[JsonSerializable(typeof(AlphaChainSettings))]
[JsonSerializable(typeof(StartPayload))]
[JsonSerializable(typeof(SubmitWordPayload))]
[JsonSerializable(typeof(OptimizationPayload))]
[JsonSerializable(typeof(SniperBanPayload))]
[JsonSerializable(typeof(TargetPayload))]
[JsonSerializable(typeof(BenchResetPayload))]
[JsonSerializable(typeof(BenchBanPayload))]
[JsonSerializable(typeof(BenchBayPayload))]
[JsonSerializable(typeof(BenchScorePayload))]
[JsonSerializable(typeof(BenchSubmitPayload))]
public partial class AlphaChainContractsJsonContext : JsonSerializerContext;
