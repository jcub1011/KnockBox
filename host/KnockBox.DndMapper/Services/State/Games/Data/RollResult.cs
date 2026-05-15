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
        DateTime TimestampUtc,
        // Compact dice-formula identifier captured from the original RollRequest,
        // e.g. "1d20", "2d6+1d8". Recorded at roll time so consumers don't have
        // to reverse-engineer it from the per-die rolls (Adv/Dis adds an extra
        // discarded die that would otherwise need to be filtered back out).
        string Formula);
}
