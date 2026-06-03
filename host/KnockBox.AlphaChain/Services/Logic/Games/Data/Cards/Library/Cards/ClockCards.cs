using KnockBox.AlphaChain.Services.Logic.Games.Data;

namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library
{
    /// <summary>×1.5 on every word, shortening the shot clock by 10%.</summary>
    public sealed class TheVaultCard : MultiplicativeCardBase, IShotClockModifier
    {
        public override ModifierId GetId() => ModifierId.TheVault;
        public override string GetName() => "The Vault";
        public override string GetIcon() => "vault";
        public override string GetDescription() => "×1.5 on every word, but shortens your shot clock by 10% while equipped.";
        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 1.5;

        public double FractionDelta => -0.10;
        public int FlatDelta => 0;
    }

    /// <summary>×2 on every word, shortening the shot clock by 20%.</summary>
    public sealed class RedlineCard : MultiplicativeCardBase, IShotClockModifier
    {
        public override ModifierId GetId() => ModifierId.Redline;
        public override string GetName() => "Redline";
        public override string GetIcon() => "redline";
        public override string GetDescription() => "×2 on every word, but shortens your shot clock by 20% while equipped.";
        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 2.0;

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
        public override string GetIcon() => "panic";
        public override string GetDescription()
            => "Halves your shot clock. ×1.35 normally — but ×2.7 if you submit before the final 2 seconds.";

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self)
            => context.RemainingShotClockDuration >= DangerZoneSeconds ? 2.7 : 1.35;

        public double FractionDelta => -0.50;
        public int FlatDelta => 0;
    }

    /// <summary>A 0-point utility (×1.0) that lengthens the shot clock — neutralises the glass-cannon clock cards.</summary>
    public sealed class HeatSinkCard : MultiplicativeCardBase, IShotClockModifier
    {
        public override ModifierId GetId() => ModifierId.HeatSink;
        public override string GetName() => "The Heat Sink";
        public override string GetIcon() => "heat-sink";
        public override string GetDescription() => "Grants 0 points but lengthens your shot clock by 30%.";
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
        public override string GetIcon() => "anchor-chain";
        public override string GetDescription()
            => "Locks your shot clock to a strict, unmodifiable 5 seconds for the era. In exchange: ×(0.5 per letter) of your word.";

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 0.5 * context.Word.Length;

        public int? GetFixedShotClockSeconds(EngineEvaluationContext context) => PinnedSeconds;
    }

    /// <summary>
    /// Hyper-Drive: inert in the scoring pipeline (it never folds), but a fast accepted submission
    /// latches an era-scoped overdrive on the owner — a short base clock plus doubled multipliers for
    /// every other card. The latch is read by <see cref="IBaseShotClockProvider"/> (clock) and
    /// <see cref="IMultiplierScaleProvider"/> (scale) while active.
    /// </summary>
    public sealed class HyperDriveCard : MultiplicativeCardBase, IBaseShotClockProvider, IMultiplierScaleProvider
    {
        /// <summary>Submit faster than this (elapsed seconds) to latch the overdrive.</summary>
        public const double ThresholdSeconds = 3;

        /// <summary>The base shot clock the overdrive imposes while latched.</summary>
        public const int OverdriveClockSeconds = 5;

        /// <summary>The multiplier scale applied to every multiplicative card while latched.</summary>
        public const double OverdriveScale = 2.0;

        public override ModifierId GetId() => ModifierId.HyperDrive;
        public override string GetName() => "Hyper-Drive";
        public override string GetIcon() => "hyper-drive";
        public override string GetDescription()
            => "Submit in under 3 seconds to overdrive: your shot clock drops to 5s for the rest of the era, but every multiplier you own is doubled.";

        // Inert in the pipeline — its power is the era latch, not a per-word fold.
        public override bool CheckIfTriggered(EngineEvaluationContext context) => false;

        public override EngineEvaluationContext OnWordAccepted(EngineEvaluationContext context, IModifierCard self)
        {
            var owner = context.GetPlayer(context.PlayerIndex);
            if (owner is { HyperDriveActive: false })
            {
                double elapsed = context.ModifiedShotClockDuration - context.RemainingShotClockDuration;
                if (elapsed < ThresholdSeconds)
                    owner.HyperDriveActive = true;
            }
            return context;
        }

        public int? GetBaseShotClockSeconds(EngineEvaluationContext context)
            => context.GetPlayer(context.PlayerIndex)?.HyperDriveActive == true ? OverdriveClockSeconds : null;

        public double GetMultiplierScale(EngineEvaluationContext context)
            => context.GetPlayer(context.PlayerIndex)?.HyperDriveActive == true ? OverdriveScale : 1.0;
    }
}
