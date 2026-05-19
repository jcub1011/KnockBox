using KnockBox.DndMapper.Models;

namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public enum RollTemplateScope { BuiltIn, Global, Sheet }

    public sealed record RollTemplate(
        Guid Id,
        string Name,
        IReadOnlyList<DiceTerm> Dice,
        int FlatModifier,
        RollMode Mode,
        string? AttributeName,
        string Label,
        RollTemplateScope Scope);
}
