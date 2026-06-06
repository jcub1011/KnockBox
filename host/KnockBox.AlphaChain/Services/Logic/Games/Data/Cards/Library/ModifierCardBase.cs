using System.Collections.Immutable;
using KnockBox.AlphaChain.Services.Logic.Games.Data;

namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library
{
    /// <summary>Score-fold primitives a card uses to express how it changes the running score, so the
    /// additive/multiplicative semantics (and Hyper-Drive's multiplier scale) live in exactly one place
    /// without a central type switch. An additive fold adds its magnitude to the running score; a
    /// multiplicative fold multiplies it by its factor, scaled by
    /// <see cref="EngineEvaluationContext.MultiplierScale"/>.</summary>
    public static class ScoreFold
    {
        /// <summary>Adds <paramref name="magnitude"/> to the running score.</summary>
        public static EngineEvaluationContext Additive(EngineEvaluationContext context, double magnitude)
            => context with { ScoreContext = context.ScoreContext.Add(magnitude) };

        /// <summary>Multiplies the running score by <paramref name="factor"/>, scaled by the
        /// context's <see cref="EngineEvaluationContext.MultiplierScale"/> (Hyper-Drive).</summary>
        public static EngineEvaluationContext Multiplicative(EngineEvaluationContext context, double factor)
            => context with { ScoreContext = context.ScoreContext.Multiply(factor * context.MultiplierScale) };
    }

    /// <summary>Shared score bound used across the pipeline.</summary>
    public static class ModifierMath
    {
        /// <summary>Upper bound on any single word's score / payout, keeping the UI sane (GDD risk note).</summary>
        public const int MaxWordScore = 10_000;

        /// <summary>Rounds half-up (away from zero) then clamps a score/payout into the legal range.</summary>
        public static int ClampScore(double value)
            => Math.Clamp((int)Math.Round(value, MidpointRounding.AwayFromZero), 0, MaxWordScore);
    }

    /// <summary>
    /// Convenience base for class-based cards: implements every <see cref="IModifierCard"/> member
    /// with sensible defaults (identity scoring via <see cref="GetMagnitude"/>, no-op lifecycle hooks)
    /// so a card overrides only what it needs and opts into capability interfaces à la carte. Simple
    /// data-defined cards use <see cref="CommonModifier"/> instead.
    /// </summary>
    public abstract class ModifierCardBase : IModifierCard
    {
        public abstract ModifierId GetId();
        public abstract string GetName();
        public abstract string GetDescription(EngineEvaluationContext context);

        /// <summary>No magnitude chip by default; a card overrides this to surface its magnitude (or "FX").</summary>
        protected virtual string? MagnitudeLabel => null;

        /// <summary>A magnitude chip when <see cref="MagnitudeLabel"/> is set (colored by add/multiply),
        /// else none. A card with live per-player status overrides this to append its own chip(s).</summary>
        public virtual ImmutableArray<CardChip> GetChips(EngineEvaluationContext context)
            => MagnitudeLabel is { } label ? [CardChips.Magnitude(label)] : [];

        public virtual bool CheckIfTriggered(EngineEvaluationContext context) => true;

        /// <summary>The effect magnification a Magnifying Glass on this card's immediate left applies to
        /// it (1.0 when none). A scoring card multiplies its magnitude by this to opt in; an inert card
        /// leaves its 1.0/0.0 magnitude alone so it is never silently turned into a multiplier.</summary>
        protected double GetMagnification(EngineEvaluationContext context)
            => context.EffectMagnifier?.GetMagnification(this) ?? 1.0;

        /// <summary>The addend (additive cards) or factor (multiplicative cards) this card contributes
        /// when triggered. Defaults to the additive identity; <see cref="MultiplicativeCardBase"/> uses 1.0.</summary>
        protected virtual double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 0.0;

        /// <summary>Folds this card additively by default; <see cref="MultiplicativeCardBase"/> multiplies instead.</summary>
        public virtual EngineEvaluationContext ExecuteModifier(EngineEvaluationContext context, IModifierCard self)
            => ScoreFold.Additive(context, GetMagnitude(context, self));

        public virtual EngineEvaluationContext OnEraStart(EngineEvaluationContext context, IModifierCard self) => context;
        public virtual EngineEvaluationContext OnWordAccepted(EngineEvaluationContext context, IModifierCard self) => context;
        public virtual EngineEvaluationContext OnTurnEnded(EngineEvaluationContext context, IModifierCard self) => context;
        public virtual EngineEvaluationContext OnOpponentWordResolved(EngineEvaluationContext context, IModifierCard self) => context;
        public virtual EngineEvaluationContext OnValidationFailed(EngineEvaluationContext context, IModifierCard self) => context;
        public virtual void SubmitMagnifications(IEffectMagnifier magnifier) { }
    }

    /// <summary>A class-based card that adds to the score (inherits the additive fold).</summary>
    public abstract class AdditiveCardBase : ModifierCardBase
    {
    }

    /// <summary>A class-based card that multiplies the score (its factor is scaled by Hyper-Drive automatically).</summary>
    public abstract class MultiplicativeCardBase : ModifierCardBase
    {
        /// <summary>The multiplicative identity, so a triggered card with no override is a no-op ×1.</summary>
        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 1.0;

        public override EngineEvaluationContext ExecuteModifier(EngineEvaluationContext context, IModifierCard self)
            => ScoreFold.Multiplicative(context, GetMagnitude(context, self));
    }
}
