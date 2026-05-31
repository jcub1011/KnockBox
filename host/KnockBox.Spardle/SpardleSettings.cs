using System.Text.Json.Serialization;
using KnockBox.Spardle.Models;

namespace KnockBox.Spardle;

/// <summary>
/// The host-configurable match rules for a Spardle game. Held by
/// <see cref="SpardleState.Settings"/> and mutated via <c>with</c> expressions inside
/// the state's execute lock. Persisted to the host's browser localStorage by the lobby
/// page so a host's preferred rules survive across sessions. The custom word pool is
/// intentionally *not* part of this record (it lives on <see cref="SpardleState"/> and
/// is excluded from persistence). Property-initializer form keeps it round-trippable by
/// System.Text.Json (Web defaults) via the parameterless constructor + init setters.
/// </summary>
public sealed record SpardleSettings
{
    // Persist enums by name, not the Web-default numeric ordinal, so reordering or
    // inserting an enum member can't silently remap a host's saved settings.
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SpardleWordSource WordPoolMode { get; init; } = SpardleWordSource.NytStandard;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WordOrderMode WordOrderMode { get; init; } = WordOrderMode.RandomNoRepeats;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WinConditionMode WinCondition { get; init; } = WinConditionMode.Sprinter;

    /// <summary>
    /// When true, the engine picks all round words at a single fixed
    /// <see cref="TargetWordLength"/>. When false, words are sampled across
    /// <see cref="MinWordLength"/>–<see cref="MaxWordLength"/> inclusive.
    /// Only consulted when <see cref="WordPoolMode"/> is
    /// <see cref="SpardleWordSource.FullDictionary"/>.
    /// </summary>
    public bool ConstantWordLength { get; init; } = true;

    /// <summary>
    /// Target word length when <see cref="ConstantWordLength"/> is true.
    /// Forced to 5 by the engine when <see cref="WordPoolMode"/> is
    /// <see cref="SpardleWordSource.NytStandard"/>. Ignored when the custom word pool is non-empty.
    /// </summary>
    public int TargetWordLength { get; init; } = 5;

    /// <summary>
    /// Minimum word length (inclusive) when <see cref="ConstantWordLength"/> is false.
    /// </summary>
    public int MinWordLength { get; init; } = 3;

    /// <summary>
    /// Maximum word length (inclusive) when <see cref="ConstantWordLength"/> is false.
    /// </summary>
    public int MaxWordLength { get; init; } = 8;

    public bool HardModeEnabled { get; init; } = false;
    public TimeSpan RoundTimer { get; init; } = TimeSpan.FromMinutes(3);
    public bool AllowDictionaryFallback { get; init; } = true;
    public bool AllowCompoundWords { get; init; } = false;
    public double DifficultyMultiplier { get; init; } = 2.0;

    public bool WaitForAll { get; init; } = true;
    public bool RevealAnswer { get; init; } = true;

    /// <summary>
    /// When true and other players are present, the host plays as a normal
    /// participant instead of becoming the display-only observer. Off by default,
    /// preserving the "host is the shared display once others join" behavior.
    /// </summary>
    public bool HostPlaysAlong { get; init; } = false;

    public int TotalRounds { get; init; } = 5;
    public TimeSpan TransitionDuration { get; init; } = TimeSpan.FromSeconds(5);
}
