namespace KnockBox.Operator.Models;

// Pure wire enums shared by the server engine/state and the browser UI. Moved here from
// the server project (KnockBox.Operator.Models, formerly in Models/Card.cs +
// Models/OperatorGamePhase.cs) so both runtimes bind identical CLR types across the
// projection/command wire. The namespace is intentionally KnockBox.Operator.Models so the
// server's existing `using KnockBox.Operator.Models;` rebinds to these with no churn.

/// <summary>The three card categories.</summary>
public enum CardType
{
    Number,
    Operator,
    Action
}

/// <summary>A player's active arithmetic operator, and the value carried by an operator card.</summary>
public enum CardOperator
{
    None,
    Add,
    Subtract,
    Multiply,
    Divide
}

/// <summary>The action-card kinds.</summary>
public enum CardAction
{
    None,
    Shield,
    LiabilityTransfer,
    CookTheBooks,
    Comp,
    Steal,
    HotPotato,
    FlashFlood,
    HostileTakeover,
    Audit,
    MarketCrash,
    Surcharge,
    BlueShell
}

/// <summary>The match phases.</summary>
public enum OperatorGamePhase
{
    Setup,
    Play,
    Reaction,
    Draw,
    GameOver
}

/// <summary>Display helper: the math symbol for an operator. Pure, used by both runtimes.</summary>
public static class CardOperatorExtensions
{
    public static string ToSymbol(this CardOperator op) => op switch
    {
        CardOperator.Add => "+",
        CardOperator.Subtract => "-",
        CardOperator.Multiply => "×",
        CardOperator.Divide => "÷",
        _ => "?"
    };
}
