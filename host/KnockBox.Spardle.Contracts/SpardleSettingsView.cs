using KnockBox.Spardle.Models;

namespace KnockBox.Spardle.Contracts;

/// <summary>
/// The host-editable match rules as they travel to/from the WASM client. Mirrors
/// the server's <c>SpardleSettings</c> minus the server-only fields: the custom
/// word pool (a large host secret that lives on the game state and is uploaded
/// separately) and <c>HostPlaysAlong</c> (a start-time choice carried in
/// <see cref="StartPayload"/>). The two <see cref="System.TimeSpan"/> durations
/// travel as whole seconds.
/// </summary>
public sealed record SpardleSettingsView
{
    public SpardleWordSource WordPoolMode { get; init; } = SpardleWordSource.NytStandard;
    public WordOrderMode WordOrderMode { get; init; } = WordOrderMode.RandomNoRepeats;
    public WinConditionMode WinCondition { get; init; } = WinConditionMode.Sprinter;

    public bool ConstantWordLength { get; init; } = true;
    public int TargetWordLength { get; init; } = 5;
    public int MinWordLength { get; init; } = 3;
    public int MaxWordLength { get; init; } = 8;

    public bool HardModeEnabled { get; init; } = false;
    public int RoundTimerSeconds { get; init; } = 180;
    public bool AllowDictionaryFallback { get; init; } = true;
    public bool AllowCompoundWords { get; init; } = false;
    public double DifficultyMultiplier { get; init; } = 2.0;

    public bool WaitForAll { get; init; } = true;
    public bool RevealAnswer { get; init; } = true;

    public int TotalRounds { get; init; } = 5;
    public int TransitionDurationSeconds { get; init; } = 5;
}
