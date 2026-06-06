using KnockBox.AlphaChain.Services.Logic.Games.Data;

namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library
{
    /// <summary>×1.5 on every word, shortening the shot clock by 10%.</summary>
    public sealed class TheVaultCard : MultiplicativeCardBase, IShotClockModifier
    {
        public override ModifierId GetId() => ModifierId.TheVault;
        public override string GetName() => "The Vault";
        public override string GetDescription(EngineEvaluationContext context) => "×1.5 on every word, but shortens your shot clock by 10% while equipped.";
        protected override string? MagnitudeLabel => "×1.5";
        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 1.5 * GetMagnification(context);

        public double FractionDelta => -0.10;
        public int FlatDelta => 0;
    }

    /// <summary>×2 on every word, shortening the shot clock by 20%.</summary>
    public sealed class RedlineCard : MultiplicativeCardBase, IShotClockModifier
    {
        public override ModifierId GetId() => ModifierId.Redline;
        public override string GetName() => "Redline";
        public override string GetDescription(EngineEvaluationContext context) => "×2 on every word, but shortens your shot clock by 20% while equipped.";
        protected override string? MagnitudeLabel => "×2";
        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 2.0 * GetMagnification(context);

        public double FractionDelta => -0.20;
        public int FlatDelta => 0;
    }

    /// <summary>Halves the shot clock; ×1.35 normally, ×2.7 when submitted before the final danger-zone seconds.</summary>
    public sealed class PanicButtonCard : MultiplicativeCardBase, IShotClockModifier
    {
        /// <summary>The final seconds of the shot clock that count as the "danger zone".</summary>
        public const double DangerZoneSeconds = 2;

        public override ModifierId GetId() => ModifierId.PanicButton;
        public override string GetName() => "Panic Button";
        public override string GetDescription(EngineEvaluationContext context)
            => "Halves your shot clock. ×1.35 normally — but ×2.7 if you submit before the final 2 seconds.";
        protected override string? MagnitudeLabel => "×1.35 – 2.7";

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self)
            => (context.RemainingShotClockDuration >= DangerZoneSeconds ? 2.7 : 1.35) * GetMagnification(context);

        public double FractionDelta => -0.50;
        public int FlatDelta => 0;
    }

    /// <summary>A 0-point utility (×1.0) that lengthens the shot clock — neutralises the glass-cannon clock cards.</summary>
    public sealed class HeatSinkCard : MultiplicativeCardBase, IShotClockModifier
    {
        public override ModifierId GetId() => ModifierId.HeatSink;
        public override string GetName() => "The Heat Sink";
        public override string GetDescription(EngineEvaluationContext context) => "Grants 0 points but lengthens your shot clock by 30%.";
        protected override string? MagnitudeLabel => "FX";
        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 1.0;

        public double FractionDelta => 0.3;
        public int FlatDelta => 0;
    }

    /// <summary>Pins the shot clock to a strict, unmodifiable length for the era, in exchange for a big per-letter multiplier.</summary>
    public sealed class AnchorChainCard : MultiplicativeCardBase, IShotClockOverride
    {
        /// <summary>The fixed, unmodifiable clock length this card pins.</summary>
        public const int PinnedSeconds = 5;

        public override ModifierId GetId() => ModifierId.AnchorChain;
        public override string GetName() => "The Anchor Chain";
        public override string GetDescription(EngineEvaluationContext context)
            => "Locks your shot clock to a strict, unmodifiable 5 seconds for the era. In exchange: ×(0.5 per letter) of your word.";
        protected override string? MagnitudeLabel => "×0.5 / ltr";

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 0.5 * context.Word.Length * GetMagnification(context);

        public int? GetFixedShotClockSeconds(EngineEvaluationContext context) => PinnedSeconds;
    }

    /// <summary>
    /// Hyper-Drive: passively caps the owner's shot clock at 5s (it only lowers a longer clock, never
    /// raises a shorter one), and — when the word is longer than 6 letters — multiplies the running
    /// score by ×1.5 at its own bay slot. Like any multiplicative card it folds in place, so the ×1.5
    /// lands on the seed plus every card to its left; cards to its right then build on the boosted
    /// total (they are not themselves multiplied). Per-word: the ×1.5 applies the turn it triggers,
    /// not for the era. The length trigger reads the Forgery-perceived letter count.
    /// </summary>
    public sealed class HyperDriveCard : MultiplicativeCardBase, IShotClockCap
    {
        /// <summary>The maximum armed clock this card caps the owner to.</summary>
        public const int CapSeconds = 5;

        /// <summary>The word must be longer than this (perceived) length to fire the multiplier.</summary>
        public const int LengthThreshold = 6;

        /// <summary>The multiplier folded into the running score at this slot when triggered.</summary>
        public const double Factor = 1.5;

        public override ModifierId GetId() => ModifierId.HyperDrive;
        public override string GetName() => "Hyper-Drive";
        public override string GetDescription(EngineEvaluationContext context)
            => "Caps your shot clock at 5s. When your word is longer than 6 letters, ×1.5 to your score so far (this slot and everything to its left).";
        protected override string? MagnitudeLabel => "×1.5";

        public override bool CheckIfTriggered(EngineEvaluationContext context)
            => this.ResolveWordLength(context) > LengthThreshold;

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => Factor * GetMagnification(context);

        public int GetShotClockCapSeconds(EngineEvaluationContext context) => CapSeconds;
    }

    /// <summary>
    /// Slow Burn: lengthens the shot clock by 20%, but forbids words shorter than 6 letters — a too-short
    /// word is illegal and takes the Zero-Point Tax (scores 0, still siphonable) exactly like a banned
    /// letter. The length floor reads the Forgery-perceived letter count. Inert in the scoring fold.
    /// </summary>
    public sealed class SlowBurnCard : MultiplicativeCardBase, IShotClockModifier, IWordLegalityRule
    {
        /// <summary>The shortest legal word length; anything shorter is taxed.</summary>
        public const int MinLength = 6;

        public override ModifierId GetId() => ModifierId.SlowBurn;
        public override string GetName() => "Slow Burn";
        public override string GetDescription(EngineEvaluationContext context)
            => "Lengthens your shot clock by 20%, but words shorter than 6 letters are illegal — they take the Zero-Point Tax.";
        protected override string? MagnitudeLabel => "FX";

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 1.0;

        public double FractionDelta => 0.20;
        public int FlatDelta => 0;

        public bool IsIllegal(EngineEvaluationContext context)
            => this.ResolveWordLength(context) < MinLength;
    }
}
