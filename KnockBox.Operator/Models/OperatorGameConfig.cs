using System;

namespace KnockBox.Operator.Models;

public class OperatorGameConfig
{
    public bool TimersEnabled { get; set; } = true;
    public TimeSpan SetupPhaseTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan PlayPhaseTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ReactionPhaseTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan DrawPhaseTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public bool EnableStacking { get; set; } = true;
    public bool FlipWinCondition { get; set; } = false;
}
