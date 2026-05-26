using System.Collections.Immutable;

namespace KnockBox.DndMapper.Services.State.Games.Data.LoadedDice
{
    // One row in the rules table. Persisted directly to LibraryCoreSnapshot;
    // polymorphic Conditions/Modifications serialize via System.Text.Json's
    // built-in discriminator support. TargetSheetIds is a set (order is
    // irrelevant); Conditions and Modifications are lists (order matters —
    // modifications compose top-to-bottom).
    public sealed record LoadedDiceRule
    {
        // Sentinel id used inside TargetSheetIds (and RollerIsCondition.SheetId)
        // to mean "the GM" — i.e. a roll that has no character sheet attached
        // (raw dice-tray rolls). Safe to overload Guid.Empty because real
        // sheets are minted with Guid.NewGuid(), which never returns the
        // all-zeroes value.
        public static readonly Guid GmTarget = Guid.Empty;

        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public bool Enabled { get; init; } = true;
        // Empty set ⇒ rule applies to every roll regardless of sheet
        // attribution. Non-empty set ⇒ rule fires only when the roll's
        // sheet id is in the set; rolls without a sheet attribution match
        // ONLY when the set contains <see cref="GmTarget"/>.
        public ImmutableHashSet<Guid> TargetSheetIds { get; init; } = ImmutableHashSet<Guid>.Empty;
        // AND-combined; empty list ⇒ matches every roll.
        public ImmutableArray<LoadedDiceCondition> Conditions { get; init; } = ImmutableArray<LoadedDiceCondition>.Empty;
        // Applied in list order to each die that the rule matches.
        public ImmutableArray<LoadedDiceModification> Modifications { get; init; } = ImmutableArray<LoadedDiceModification>.Empty;
    }
}
