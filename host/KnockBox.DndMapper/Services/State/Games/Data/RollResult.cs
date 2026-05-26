using System.Collections.Immutable;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.State.Games.Data.LoadedDice;

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
        string Formula,
        // Human-readable breakdown when status effects contributed to the
        // attribute modifier — e.g. "12 + 2 (INT) − 5 (Brain Fog) = 9". Null
        // when no effects contributed; rendering falls back to the plain total.
        string? ModifierBreakdown = null,
        // Token whose dice these are when the roll is *for* a specific
        // token rather than a user — used by NPC initiative rolls so each
        // NPC gets its own concurrent 3D-dice instance (keyed by token id)
        // and its dice render in the token's resolved color. Null for
        // player rolls; the existing RollerUserId path handles those.
        Guid? TokenId = null)
    {
        // Loaded-dice audit: rules that fired during this roll. Empty when
        // the master toggle was off or no rule matched. The list is what the
        // roll log uses to render the "Loaded" badge — historical rolls keep
        // their stamps even if the underlying rule is later edited or deleted.
        public ImmutableArray<LoadedDiceRuleStamp> AppliedRules { get; init; }
            = ImmutableArray<LoadedDiceRuleStamp>.Empty;

        // Original dice composition from the inbound RollRequest, captured so
        // the roll log's re-roll affordance can faithfully repeat the roll.
        // Reconstructing this from Rolls is brittle: Adv/Dis adds a discarded
        // twin that would have to be filtered back out. Empty list ⇒ the
        // record was deserialized from a pre-feature save and re-roll is
        // unavailable (button is rendered disabled).
        public ImmutableArray<DiceTerm> OriginalDice { get; init; } = ImmutableArray<DiceTerm>.Empty;

        // Sheet + attribute the request bound to, for the re-roll path. The
        // resolved AttributeModifier above is enough for display but loses the
        // sheet identity (needed by loaded-dice context and "rolling as X" UX).
        public AttributeRef? OriginalAttributeRef { get; init; }
    }
}
