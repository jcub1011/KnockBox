using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;

namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library
{
    /// <summary>Tax Collector — inert in scoring; collects a cut when an opponent's word is era-taxed.</summary>
    public sealed class TaxCollectorCard : MultiplicativeCardBase
    {
        /// <summary>Fraction of an opponent's taxed-away score this card collects.</summary>
        public const double Rate = 0.5;

        public override ModifierId GetId() => ModifierId.TaxCollector;
        public override string GetName() => "Tax Collector";
        public override string GetDescription()
            => "When an opponent plays a banned-letter word, collect half the points it would have scored.";

        public override bool CheckIfTriggered(EngineEvaluationContext context) => false;

        public override EngineEvaluationContext OnOpponentWordResolved(EngineEvaluationContext context, IModifierCard self)
        {
            var res = context.Resolution;
            if (res is null || !res.Taxed || res.SiphonSuppressed || res.WouldBeScore <= 0)
                return context;

            var owner = context.GetPlayer(context.PlayerIndex);
            if (owner is null || owner.UserId == res.SubmitterUserId)
                return context;

            int amount = ModifierMath.ClampScore(res.WouldBeScore * Rate);
            if (amount <= 0) return context;

            owner.Score += amount;
            context.Service<IEngineEffects>()?.RecordEraTaxSiphon(owner.DisplayName, amount);
            return context;
        }
    }

    /// <summary>The Toll Booth — inert in scoring; rolls a personal ban each era and tolls opponents who use it.</summary>
    public sealed class TollBoothCard : MultiplicativeCardBase, IContributesRoomServices
    {
        /// <summary>Fraction of an opponent's earned score this card mints when they use its rolled ban letter.</summary>
        public const double Rate = 0.20;

        public override ModifierId GetId() => ModifierId.TollBooth;
        public override string GetName() => "The Toll Booth";
        public override string GetDescription()
            => "Each era, rolls you a personal banned letter (Zero-Point Tax if you use it). Toll: bank 20% of any opponent's score when their word uses that letter.";

        public override bool CheckIfTriggered(EngineEvaluationContext context) => false;

        public override EngineEvaluationContext OnEraStart(EngineEvaluationContext context, IModifierCard self)
        {
            var owner = context.GetPlayer(context.PlayerIndex);
            if (owner is not null && context.Service<IBanLetterService>()?.RollPersonalBan() is { } ban)
                context.Service<ICardBanService>()?.Roll(owner, GetId(), ban);
            return context;
        }

        public override EngineEvaluationContext OnOpponentWordResolved(EngineEvaluationContext context, IModifierCard self)
        {
            var res = context.Resolution;
            if (res is null || res.Taxed || res.EarnedScore <= 0)
                return context;

            var owner = context.GetPlayer(context.PlayerIndex);
            if (owner is null || owner.UserId == res.SubmitterUserId)
                return context;

            if (context.Service<ICardBanService>()?.BanFor(owner, GetId()) is { } banned && res.Word.Contains(banned))
            {
                int amount = ModifierMath.ClampScore(res.EarnedScore * Rate);
                if (amount > 0) owner.Score += amount;
            }
            return context;
        }

        public IEnumerable<RoomServiceDescriptor> GetRoomServices()
            => [new(typeof(ICardBanService), static _ => new CardBanService())];
    }

    /// <summary>The Roulette Wheel — ×1.75 on every clean word; rolls a personal ban each era.</summary>
    public sealed class RouletteWheelCard : MultiplicativeCardBase, IContributesRoomServices
    {
        public override ModifierId GetId() => ModifierId.RouletteWheel;
        public override string GetName() => "The Roulette Wheel";
        public override string GetDescription()
            => "Each era, rolls you a personal banned letter (Zero-Point Tax if you use it). Reward: ×1.75 on every word you keep clean.";
        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 1.75;

        public override EngineEvaluationContext OnEraStart(EngineEvaluationContext context, IModifierCard self)
        {
            var owner = context.GetPlayer(context.PlayerIndex);
            if (owner is not null && context.Service<IBanLetterService>()?.RollPersonalBan() is { } ban)
                context.Service<ICardBanService>()?.Roll(owner, GetId(), ban);
            return context;
        }

        public IEnumerable<RoomServiceDescriptor> GetRoomServices()
            => [new(typeof(ICardBanService), static _ => new CardBanService())];
    }

    /// <summary>The Bounty Hunter — inert (×1.0); docks the round leader on a too-short word.</summary>
    public sealed class BountyHunterCard : MultiplicativeCardBase
    {
        /// <summary>Words shorter than this length expose the leader to the penalty.</summary>
        public const int MinLength = 6;

        /// <summary>Points docked from the leader on a too-short word.</summary>
        public const int Penalty = 15;

        public override ModifierId GetId() => ModifierId.BountyHunter;
        public override string GetName() => "The Bounty Hunter";
        public override string GetDescription()
            => "Grants 0 points. Marks the leader each round — if they play a word shorter than 6 letters, they lose 15 points.";
        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 1.0;

        public override EngineEvaluationContext OnOpponentWordResolved(EngineEvaluationContext context, IModifierCard self)
        {
            var res = context.Resolution;
            var effects = context.Service<IEngineEffects>();
            if (res is null || effects is null) return context;
            if (res.SubmitterUserId != effects.RoundLeaderUserId || res.Word.Length >= MinLength)
                return context;

            var owner = context.GetPlayer(context.PlayerIndex);
            var leader = context.GetPlayer(context.GetPlayerIndex(res.SubmitterUserId));
            if (owner is null || leader is null || owner.UserId == leader.UserId)
                return context;

            effects.Drain(self, owner, leader, Penalty);
            return context;
        }
    }

    /// <summary>Flak Cannon — 0 points; shaves the next clock of every player scoring higher than the owner.</summary>
    public sealed class FlakCannonCard : AdditiveCardBase, IContributesRoomServices
    {
        /// <summary>Seconds shaved off each higher-scoring opponent's next clock.</summary>
        public const int ShaveSeconds = 2;

        public override ModifierId GetId() => ModifierId.FlakCannon;
        public override string GetName() => "Flak Cannon";
        public override string GetDescription()
            => "Grants 0 points. Takes 2 seconds off the next shot clock of every player scoring higher than you.";

        public override EngineEvaluationContext OnTurnEnded(EngineEvaluationContext context, IModifierCard self)
        {
            var owner = context.GetPlayer(context.PlayerIndex);
            var effects = context.Service<IEngineEffects>();
            if (owner is null || effects is null) return context;

            foreach (var opp in effects.OrderedActivePlayers())
                if (opp.UserId != owner.UserId && opp.Score > owner.Score)
                    effects.TimeShave(self, owner, opp, ShaveSeconds);

            return context;
        }

        // The time-shave it fires lands on a victim who doesn't hold this card, so the service must
        // exist room-wide regardless of who's dealt the cannon (the catalogue-union guarantees that).
        public IEnumerable<RoomServiceDescriptor> GetRoomServices()
            => [new(typeof(ITimePenaltyService), static _ => new TimePenaltyService())];
    }

    /// <summary>Bait &amp; Switch — inert; on a taxed word, curses the next player with the offending letter.</summary>
    public sealed class BaitAndSwitchCard : MultiplicativeCardBase, IContributesRoomServices
    {
        public override ModifierId GetId() => ModifierId.BaitAndSwitch;
        public override string GetName() => "Bait & Switch";
        public override string GetDescription()
            => "When your word is hit by the Zero-Point Tax, curse the next player with that exact banned letter for their next turn.";

        public override bool CheckIfTriggered(EngineEvaluationContext context) => false;

        public override EngineEvaluationContext OnTurnEnded(EngineEvaluationContext context, IModifierCard self)
        {
            var res = context.Resolution;
            if (res is null || !res.Taxed || res.OffendingLetter is not { } letter)
                return context;

            var owner = context.GetPlayer(context.PlayerIndex);
            var effects = context.Service<IEngineEffects>();
            if (owner is null || effects is null) return context;

            var next = effects.PeekNextActivePlayer(owner.UserId);
            if (next is not null)
                effects.LetterHijack(self, owner, next, letter);

            return context;
        }

        // The hijack ban it inflicts lands on the next player, who doesn't hold this card, so the
        // service must exist room-wide regardless of who's dealt Bait & Switch.
        public IEnumerable<RoomServiceDescriptor> GetRoomServices()
            => [new(typeof(IHijackBanService), static _ => new HijackBanService())];
    }
}
