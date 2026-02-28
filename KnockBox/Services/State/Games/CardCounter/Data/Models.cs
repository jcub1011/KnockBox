namespace KnockBox.Services.State.Games.CardCounter.Data
{
    public enum GamePhase { BuyIn, Playing, RoundEnd, GameOver }

    public enum CardType { Number, Operator }

    public abstract record Card(CardType Type);

    public record NumberCard(int Value) : Card(CardType.Number);

    public enum Operator { Add, Subtract, Multiply, Divide }

    public record OperatorCard(Operator Op) : Card(CardType.Operator);

    public enum ActionType { FeelingLucky, MakeMyLuck, Skim, Burn, TurnTheTable, Compd, NotMyMoney, Launder }

    public record ActionCard(ActionType Action);
    
    public enum ActionKind { SetBuyIn, PlayActionCard, DiscardExcess, Fold, Draw, Pass, ChooseBuyInSign, AcceptPending, ReorderMakeMyLuck }

    public class PlayerAction
    {
        public ActionKind ActionKind { get; set; }
        public Dictionary<string, object> Data { get; set; } = new();
    }

    public class GameConfig
    {
        public int DeckSize { get; set; } = 52;
        public float NumberToOperatorRatio { get; set; } = 4.0f;
        public float AddSubToMulDivRatio { get; set; } = 4.0f;
        public int ActionsDealtPerRound { get; set; } = 3;
        public int ActionHandLimit { get; set; } = 6;
        public int TotalPassesPerPlayer { get; set; } = 3;
        public int MinShoeSize { get; set; } = 12;
        public int MaxShoeSize { get; set; } = 20;
        public int ActionResponseTimeoutMs { get; set; } = 15000;
    }

    public class ForcedDrawChain
    {
        public string OriginatorId { get; set; } = string.Empty;
        public string CurrentTargetId { get; set; } = string.Empty;
        public List<string> ChainParticipants { get; set; } = new();
    }

    public class PlayerState
    {
        public string PlayerId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public int Balance { get; set; }
        public List<int> Pot { get; set; } = new();
        public int PotValue 
        { 
            get 
            {
                if (Pot.Count == 0) return 0;
                string s = string.Join("", Pot);
                return int.TryParse(s, out int value) ? value : 0;
            }
        }
        public List<ActionCard> ActionHand { get; set; } = new();
        public int PassesRemaining { get; set; }
        public bool IsHost { get; set; }
        public bool HasSetBuyIn { get; set; }
        public int BuyInRoll { get; set; }
        public List<Card>? PrivateReveal { get; set; }
    }

    public class PendingAction
    {
        public string SourcePlayerId { get; set; } = string.Empty;
        public string TargetPlayerId { get; set; } = string.Empty;
        public ActionCard CardPlayed { get; set; } = default!;
        public ActionCard? CounteredBy { get; set; }
        public DateTimeOffset ExpirationTime { get; set; }
        public Dictionary<string, object> Data { get; set; } = new();
    }
}