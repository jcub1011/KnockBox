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
    /// <param name="ModifierType">Whether <see cref="MagnitudeProvider"/> is an addend or a factor.</param>
    /// <param name="TriggerChecker">Whether the card contributes for the current word.</param>
    /// <param name="MagnitudeProvider">The addend (additive) or factor (multiplicative) when triggered.</param>
    /// <param name="MagnitudeLabel">A short, glanceable chip label (e.g. "+10", "×0.5–2"); null hides the chip.</param>
    public readonly record struct CommonModifier(
        ModifierId Id,
        string Name,
        string Description,
        ModifierType ModifierType,
        Func<EngineEvaluationContext, bool> TriggerChecker,
        Func<EngineEvaluationContext, IModifierCard, double> MagnitudeProvider,
        string? MagnitudeLabel = null)
        : IModifierCard
    {
        public ModifierId GetId() => Id;

        public string GetName() => Name;

        public string GetDescription() => Description;

        public string GetDescription(EngineEvaluationContext context) => Description;

        public ModifierType GetModifierType(EngineEvaluationContext context) => ModifierType;

        public string? GetMagnitudeLabel() => MagnitudeLabel;

        public bool CheckIfTriggered(EngineEvaluationContext context) => TriggerChecker.Invoke(context);

        public EngineEvaluationContext ExecuteModifier(EngineEvaluationContext context, IModifierCard self)
            => ModifierMath.Apply(context, ModifierType, MagnitudeProvider.Invoke(context, self));
    }
}
