namespace KnockBox.HiddenAgenda.Services.State.Games.Data;

public enum TaskPoolRotation { Full, Partial, Fixed }

public class HiddenAgendaGameConfig
{
    public int TotalRounds { get; set; } = 4;
    public int RoundSetupTimeoutMs { get; set; } = 10000;
    public int EventCardPhaseTimeoutMs { get; set; } = 10000;
    public int SpinPhaseTimeoutMs { get; set; } = 10000;
    public int MovePhaseTimeoutMs { get; set; } = 15000;
    public int DrawPhaseTimeoutMs { get; set; } = 15000;
    public int GuessPhaseTimeoutMs { get; set; } = 60000;
    public int FinalGuessTimeoutMs { get; set; } = 45000;
    public int RevealTimeoutMs { get; set; } = 15000;
    public bool EnableTimers { get; set; } = false;
    public TaskPoolRotation PoolRotation { get; set; } = TaskPoolRotation.Partial;
}
