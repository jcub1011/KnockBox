using System.Collections.Immutable;
using KnockBox.AlphaChain.Services.Logic.Games.Data;

namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library
{
    /// <summary>
    /// Shared scoring fold used by every card implementation, so the additive/multiplicative
    /// semantics (and Hyper-Drive's multiplier scale) live in exactly one place. An additive card
    /// adds its magnitude to the running value; a multiplicative card multiplies the running value by
    /// its factor, scaled by <see cref="EngineEvaluationContext.MultiplierScale"/>.
    /// </summary>
    public static class ModifierMath
    {
        /// <summary>Upper bound on any single word's score / payout, keeping the UI sane (GDD risk note).</summary>
        public const int MaxWordScore = 10_000;

        /// <summary>Folds <paramref name="magnitude"/> into the context's running value per <paramref name="type"/>.</summary>
        public static EngineEvaluationContext Apply(EngineEvaluationContext context, ModifierType type, double magnitude)
            => type == ModifierType.Additive
                ? context with { ValueToAdd = context.ValueToAdd + magnitude }
                : context with { ValueToAdd = context.ValueToAdd * magnitude * context.MultiplierScale };

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

        /// <summary>Additive by default; <see cref="MultiplicativeCardBase"/> flips this.</summary>
        public virtual ModifierType GetModifierType() => ModifierType.Additive;

        /// <summary>No magnitude chip by default; a card overrides this to surface its magnitude (or "FX").</summary>
        protected virtual string? MagnitudeLabel => null;

        /// <summary>A magnitude chip when <see cref="MagnitudeLabel"/> is set (colored by add/multiply),
        /// else none. A card with live per-player status overrides this to append its own chip(s).</summary>
        public virtual ImmutableArray<CardChip> GetChips(EngineEvaluationContext context)
            => MagnitudeLabel is { } label ? [CardChips.Magnitude(label)] : [];

        public virtual bool CheckIfTriggered(EngineEvaluationContext context) => true;

        /// <summary>The addend (additive cards) or factor (multiplicative cards) this card contributes
        /// when triggered. Defaults to the additive/multiplicative identity.</summary>
        protected virtual double GetMagnitude(EngineEvaluationContext context, IModifierCard self)
            => GetModifierType() == ModifierType.Additive ? 0.0 : 1.0;

        public virtual EngineEvaluationContext ExecuteModifier(EngineEvaluationContext context, IModifierCard self)
            => ModifierMath.Apply(context, GetModifierType(), GetMagnitude(context, self));

        public virtual EngineEvaluationContext OnEraStart(EngineEvaluationContext context, IModifierCard self) => context;
        public virtual EngineEvaluationContext OnWordAccepted(EngineEvaluationContext context, IModifierCard self) => context;
        public virtual EngineEvaluationContext OnTurnEnded(EngineEvaluationContext context, IModifierCard self) => context;
        public virtual EngineEvaluationContext OnOpponentWordResolved(EngineEvaluationContext context, IModifierCard self) => context;
        public virtual EngineEvaluationContext OnValidationFailed(EngineEvaluationContext context, IModifierCard self) => context;
    }

    /// <summary>A class-based card that adds to the score.</summary>
    public abstract class AdditiveCardBase : ModifierCardBase
    {
        public sealed override ModifierType GetModifierType() => ModifierType.Additive;
    }

    /// <summary>A class-based card that multiplies the score (its factor is scaled by Hyper-Drive automatically).</summary>
    public abstract class MultiplicativeCardBase : ModifierCardBase
    {
        public sealed override ModifierType GetModifierType() => ModifierType.Multiplier;
    }
}
