using System;
using KnockBox.Operator.Models;

namespace KnockBox.Operator.Services.Projection;

/// <summary>
/// Maps between the authoritative server <see cref="OperatorSettings"/> and the
/// host-surfaced <see cref="OperatorSettingsView"/> the lobby drawer edits. Timeouts are
/// whole seconds on the wire. <see cref="Apply"/> preserves every server-only field
/// (hand/draw limits, initial points, NoReactionTimeout, EnableStacking, HostPlays) so an
/// edit from the lobby never silently resets them.
/// </summary>
public static class OperatorSettingsMapping
{
    public static OperatorSettingsView ToView(OperatorSettings s) => new()
    {
        TimersEnabled = s.TimersEnabled,
        SetupPhaseSeconds = (int)Math.Round(s.SetupPhaseTimeout.TotalSeconds),
        PlayPhaseSeconds = (int)Math.Round(s.PlayPhaseTimeout.TotalSeconds),
        ReactionPhaseSeconds = (int)Math.Round(s.ReactionPhaseTimeout.TotalSeconds),
        FlipWinCondition = s.FlipWinCondition,
    };

    /// <summary>
    /// Folds an incoming view onto the current settings, clamping timeouts to a sane range
    /// and carrying every non-surfaced field through unchanged.
    /// </summary>
    public static OperatorSettings Apply(OperatorSettings current, OperatorSettingsView view) =>
        current with
        {
            TimersEnabled = view.TimersEnabled,
            SetupPhaseTimeout = TimeSpan.FromSeconds(Clamp(view.SetupPhaseSeconds)),
            PlayPhaseTimeout = TimeSpan.FromSeconds(Clamp(view.PlayPhaseSeconds)),
            ReactionPhaseTimeout = TimeSpan.FromSeconds(Clamp(view.ReactionPhaseSeconds)),
            FlipWinCondition = view.FlipWinCondition,
        };

    private static int Clamp(int seconds) => Math.Clamp(seconds, 5, 300);
}
