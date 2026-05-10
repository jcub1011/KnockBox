using KnockBox.DndMapper.Models;

namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed record DieRoll(int Sides, int Result, bool Discarded);

    public sealed record RollResult(
        Guid Id,
        string RollerUserId,
        string? ForcedByUserId,
        IReadOnlyList<DieRoll> Rolls,
        int Total,
        RollMode Mode,
        int FlatModifier,
        int? AttributeModifier,
        string Label,
        DateTime TimestampUtc);
}
