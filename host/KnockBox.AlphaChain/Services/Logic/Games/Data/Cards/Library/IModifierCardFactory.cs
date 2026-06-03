namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library
{
    public interface IModifierCardFactory
    {
        IModifierCard CreateCard(EngineEvaluationContext context, ModifierId modifier);
    }
}
