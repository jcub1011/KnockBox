using System;

namespace KnockBox.Operator.Models;

public class OperatorGameConfig
{
    public bool TimersEnabled { get; set; } = true;
    public TimeSpan SetupPhaseTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan PlayPhaseTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ReactionPhaseTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public TimeSpan DrawPhaseTimeout { get; set; } = TimeSpan.FromSeconds(15);
    public int MaxHandSize { get; set; } = 5;
    public int MaxDrawPerTurn { get; set; } = 3;
    public decimal InitialPointsPositive { get; set; } = 10m;
    public decimal InitialPointsNegative { get; set; } = -10m;
    public TimeSpan NoReactionTimeout { get; set; } = TimeSpan.FromSeconds(5);
    public bool EnableStacking { get; set; } = true;
    public bool FlipWinCondition { get; set; } = false;
}
