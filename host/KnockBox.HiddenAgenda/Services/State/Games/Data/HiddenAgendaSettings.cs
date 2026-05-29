using System.Text.Json.Serialization;

namespace KnockBox.HiddenAgenda.Services.State.Games.Data;

public enum TaskPoolRotation { Full, Partial, Fixed }

public sealed record HiddenAgendaSettings
{
    public int TotalRounds { get; init; } = 4;
    public int RoundSetupTimeoutMs { get; init; } = 10000;
    public int EventCardPhaseTimeoutMs { get; init; } = 10000;
    public int SpinPhaseTimeoutMs { get; init; } = 10000;
    public int MovePhaseTimeoutMs { get; init; } = 15000;
    public int DrawPhaseTimeoutMs { get; init; } = 15000;
    public int GuessPhaseTimeoutMs { get; init; } = 60000;
    public int FinalGuessTimeoutMs { get; init; } = 45000;
    public int RevealTimeoutMs { get; init; } = 15000;
    public bool EnableTimers { get; init; } = false;

    // Persisted snapshots must survive enum reordering, so serialize by name.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TaskPoolRotation PoolRotation { get; init; } = TaskPoolRotation.Partial;
}
