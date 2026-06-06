using System.Collections.Immutable;
using KnockBox.AlphaChain.Services.Logic.Games.Data;

namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library
{
    /// <summary>
    /// The data-defined card path: a card whose entire behavior is a static description, a trigger
    /// predicate, and a magnitude (its additive bonus or multiplicative factor). Cards needing a
    /// lifecycle hook or a capability interface are written as a class deriving
    /// <see cref="ModifierCardBase"/> instead.
    /// </summary>
    /// <param name="Id">The card's stable identity.</param>
    /// <param name="Name">Display name.</param>
    /// <param name="Description">Static rules text.</param>
    /// <param name="Fold">How the card folds its magnitude into the running score — typically
    /// <see cref="ScoreFold.Additive"/> or <see cref="ScoreFold.Multiplicative"/>.</param>
    /// <param name="TriggerChecker">Whether the card contributes for the current word.</param>
    /// <param name="MagnitudeProvider">The addend (additive) or factor (multiplicative) when triggered.</param>
    /// <param name="MagnitudeLabel">A short, glanceable chip label (e.g. "+10", "×0.5–2"); null hides the chip.</param>
    public readonly record struct CommonModifier(
        ModifierId Id,
        string Name,
        string Description,
        Func<EngineEvaluationContext, double, EngineEvaluationContext> Fold,
        Func<EngineEvaluationContext, bool> TriggerChecker,
        Func<EngineEvaluationContext, IModifierCard, double> MagnitudeProvider,
        string? MagnitudeLabel = null)
        : IModifierCard
    {
        public ModifierId GetId() => Id;

        public string GetName() => Name;

        public string GetDescription(EngineEvaluationContext context) => Description;

        public ImmutableArray<CardChip> GetChips(EngineEvaluationContext context)
            => MagnitudeLabel is { } label ? [CardChips.Magnitude(label)] : [];

        public bool CheckIfTriggered(EngineEvaluationContext context) => TriggerChecker.Invoke(context);

        public EngineEvaluationContext ExecuteModifier(EngineEvaluationContext context, IModifierCard self)
        {
            // Every CommonModifier is a real scoring card, so it always opts into magnification: a
            // Magnifying Glass on its immediate left scales its magnitude directly (+10 → +15, ×2 → ×3).
            double magnitude = MagnitudeProvider.Invoke(context, self) * (context.EffectMagnifier?.GetMagnification(self) ?? 1.0);
            return Fold.Invoke(context, magnitude);
        }
    }
}
