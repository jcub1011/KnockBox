using KnockBox.CardCounter.Contracts;

namespace KnockBox.CardCounter.Client;

/// <summary>
/// Pure presentation helpers ported from the server pages (NumberFormatExtensions +
/// PlayingPhase static helpers). No server dependencies — safe in the WASM client.
/// </summary>
public static class CardDisplay
{
    public static string FormatBalance(this double balance)
    {
        if (Math.Abs(balance) < 1000000.0)
            return balance >= 0 ? $"+{balance:N0}" : $"{balance:N0}";
        return balance >= 0 ? $"+{balance:E4}" : $"{balance:E4}";
    }

    public static bool RequiresTarget(ActionType action) => action switch
    {
        ActionType.Skim => true,
        ActionType.TurnTheTable => true,
        ActionType.Launder => true,
        _ => false
    };

    public static string GetActionCardName(ActionType action) => action switch
    {
        ActionType.FeelingLucky => "Feeling Lucky",
        ActionType.MakeMyLuck => "Make My Luck",
        ActionType.Skim => "Skim",
        ActionType.Burn => "Burn",
        ActionType.TurnTheTable => "Turn The Table",
        ActionType.Compd => "Comp'd",
        ActionType.NotMyMoney => "Not My Money",
        ActionType.Launder => "Launder",
        ActionType.Tilt => "Tilt",
        ActionType.HedgeYourBet => "Hedge Your Bet",
        ActionType.LetItRide => "Let It Ride",
        _ => action.ToString()
    };

    public static string GetActionCardIcon(ActionType action) => action switch
    {
        ActionType.FeelingLucky => "🎲",
        ActionType.MakeMyLuck => "⭐",
        ActionType.Skim => "✂️",
        ActionType.Burn => "🔥",
        ActionType.TurnTheTable => "🔄",
        ActionType.Compd => "🛡️",
        ActionType.NotMyMoney => "💸",
        ActionType.Launder => "🧺",
        ActionType.Tilt => "🎰",
        ActionType.HedgeYourBet => "🎯",
        ActionType.LetItRide => "🔁",
        _ => "🃏"
    };

    public static string GetActionCardDescription(ActionType action) => action switch
    {
        ActionType.FeelingLucky =>
            "Force the next player to draw a card from the shoe. They may chain it with their own Feeling Lucky or block with Comp'd.",
        ActionType.MakeMyLuck =>
            "Peek at the top 3 cards in the shoe and rearrange them in any order you choose.",
        ActionType.Skim =>
            "Swap any digit in your pot with any digit in a chosen opponent's pot. Cannot be played or target players with empty pots. Blockable with Comp'd.",
        ActionType.Burn =>
            "Discard the top card of the shoe without drawing it. Useful for removing dangerous cards.",
        ActionType.TurnTheTable =>
            "Reverse the digit order of a chosen opponent's pot. Target required. Blockable with Comp'd.",
        ActionType.Compd =>
            "Block a card played against you (Feeling Lucky, Skim, Turn The Table, or Launder). Hold until needed.",
        ActionType.NotMyMoney =>
            "When you draw an operator card, redirect it to apply to another player's pot instead.",
        ActionType.Launder =>
            "Swap your entire pot with a chosen opponent's pot. Target required. Blockable with Comp'd.",
        ActionType.Tilt =>
            "Shuffle all number cards from every player's pot into one pool, then redistribute them evenly. Extra cards are dealt one at a time starting from you, in turn order.",
        ActionType.HedgeYourBet =>
            "Convert the next card drawn from the shoe into a + if your balance is negative, or a − if your balance is zero or positive. Does not draw immediately. Only playable when the shoe is not empty.",
        ActionType.LetItRide =>
            "Grant yourself an extra turn after your current turn ends. Can be stacked: each card played adds one additional turn.",
        _ => action.ToString()
    };

    public static string GetActionCardColor(ActionType action) => action switch
    {
        ActionType.FeelingLucky => "cc-card-lucky",
        ActionType.MakeMyLuck => "cc-card-luck",
        ActionType.Skim => "cc-card-skim",
        ActionType.Burn => "cc-card-burn",
        ActionType.TurnTheTable => "cc-card-turn",
        ActionType.Compd => "cc-card-compd",
        ActionType.NotMyMoney => "cc-card-money",
        ActionType.Launder => "cc-card-launder",
        ActionType.Tilt => "cc-card-tilt",
        ActionType.HedgeYourBet => "cc-card-hedge",
        ActionType.LetItRide => "cc-card-letitride",
        _ => ""
    };

    public static string GetOperatorSymbol(Operator op) => op switch
    {
        Operator.Add => "+",
        Operator.Subtract => "−",
        Operator.Multiply => "×",
        Operator.Divide => "÷",
        _ => "?"
    };

    public static string FormatBaseCardDisplay(BaseCard card) => card switch
    {
        NumberCard nc => $"{nc.Value}",
        OperatorCard oc => GetOperatorSymbol(oc.Op),
        _ => "?"
    };

    public static string GetBaseCardTypeLabel(BaseCard card) => card switch
    {
        NumberCard => "NUMBER",
        OperatorCard => "OPERATOR",
        _ => "CARD"
    };

    public static string GetOperatorName(Operator op) => op switch
    {
        Operator.Add => "Add",
        Operator.Subtract => "Subtract",
        Operator.Multiply => "Multiply",
        Operator.Divide => "Divide",
        _ => "?"
    };
}
