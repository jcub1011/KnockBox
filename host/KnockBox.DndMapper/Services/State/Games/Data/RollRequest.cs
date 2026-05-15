using KnockBox.DndMapper.Models;

namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed record RollRequest(
        IReadOnlyList<DiceTerm> Dice,
        AttributeRef? AttributeRef,
        int FlatModifier,
        RollMode Mode,
        string Label);
}
