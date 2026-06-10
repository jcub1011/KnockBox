using KnockBox.Spardle.Contracts;

namespace KnockBox.Spardle.Services.Projection;

/// <summary>
/// Maps between the server-authoritative <see cref="SpardleSettings"/> (which keeps
/// the start-time <c>HostPlaysAlong</c> flag and holds the two durations as
/// <see cref="System.TimeSpan"/>) and the wire <see cref="SpardleSettingsView"/> the
/// client edits/displays. The view carries only the host-editable knobs; applying
/// it preserves every server-only field via <c>with</c>. The custom word pool is
/// not a setting (it lives on <see cref="SpardleState"/> and is uploaded separately).
/// </summary>
internal static class SpardleSettingsMapping
{
    public static SpardleSettingsView ToView(this SpardleSettings s) => new()
    {
        WordPoolMode = s.WordPoolMode,
        WordOrderMode = s.WordOrderMode,
        WinCondition = s.WinCondition,
        ConstantWordLength = s.ConstantWordLength,
        TargetWordLength = s.TargetWordLength,
        MinWordLength = s.MinWordLength,
        MaxWordLength = s.MaxWordLength,
        HardModeEnabled = s.HardModeEnabled,
        RoundTimerSeconds = (int)s.RoundTimer.TotalSeconds,
        AllowDictionaryFallback = s.AllowDictionaryFallback,
        AllowCompoundWords = s.AllowCompoundWords,
        DifficultyMultiplier = s.DifficultyMultiplier,
        WaitForAll = s.WaitForAll,
        RevealAnswer = s.RevealAnswer,
        TotalRounds = s.TotalRounds,
        TransitionDurationSeconds = (int)s.TransitionDuration.TotalSeconds,
    };

    /// <summary>
    /// Returns a copy of <paramref name="s"/> with the host-editable fields replaced
    /// from <paramref name="v"/> (clamped), leaving the server-only
    /// <c>HostPlaysAlong</c> untouched.
    /// </summary>
    public static SpardleSettings Apply(this SpardleSettings s, SpardleSettingsView v) => s with
    {
        WordPoolMode = v.WordPoolMode,
        WordOrderMode = v.WordOrderMode,
        WinCondition = v.WinCondition,
        ConstantWordLength = v.ConstantWordLength,
        TargetWordLength = Math.Clamp(v.TargetWordLength, 1, 64),
        MinWordLength = Math.Clamp(v.MinWordLength, 1, 64),
        MaxWordLength = Math.Clamp(v.MaxWordLength, 1, 64),
        HardModeEnabled = v.HardModeEnabled,
        RoundTimer = TimeSpan.FromSeconds(Math.Max(0, v.RoundTimerSeconds)),
        AllowDictionaryFallback = v.AllowDictionaryFallback,
        AllowCompoundWords = v.AllowCompoundWords,
        DifficultyMultiplier = Math.Clamp(v.DifficultyMultiplier, 0.0, 10.0),
        WaitForAll = v.WaitForAll,
        RevealAnswer = v.RevealAnswer,
        TotalRounds = Math.Max(1, v.TotalRounds),
        TransitionDuration = TimeSpan.FromSeconds(Math.Clamp(v.TransitionDurationSeconds, 0, 60)),
        // HostPlaysAlong preserved (not on the view — it's a start-time choice).
    };
}
