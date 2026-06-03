namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library
{
    public interface IModifierCardFactory
    {
        IModifierCard CreateCard(EngineEvaluationContext context, ModifierId modifier);

        /// <summary>Every room state service declared across the whole card catalogue (the union the
        /// per-room container instantiates, so a card's service exists even when it writes to an
        /// opponent who doesn't hold it). Duplicate contracts are collapsed by the container.</summary>
        IEnumerable<RoomServiceDescriptor> AllCardRoomServices();
    }
}
