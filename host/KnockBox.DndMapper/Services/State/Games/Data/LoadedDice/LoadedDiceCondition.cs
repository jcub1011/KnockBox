using System.Collections.Immutable;
using System.Text.Json.Serialization;
using KnockBox.DndMapper.Models;

namespace KnockBox.DndMapper.Services.State.Games.Data.LoadedDice
{
    // Strategy base for "when X" filters that gate a rule. Concrete subtypes
    // are tagged with [JsonDerivedType] so System.Text.Json round-trips them
    // through LibrarySnapshot without a custom converter. Adding a new
    // condition: declare the record, register it on this attribute list, and
    // add its editor component to LoadedDiceUiRegistry.
    //
    // Discriminator strings ("$kind" values) are stable persistence keys —
    // renaming a record is fine, but never change its existing string here
    // or every saved rule referencing it breaks at load time.
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
    [JsonDerivedType(typeof(CurrentMapCondition), "currentMap")]
    [JsonDerivedType(typeof(DiceTypeRolledCondition), "diceTypeRolled")]
    [JsonDerivedType(typeof(RollerIsCondition), "rollerIs")]
    [JsonDerivedType(typeof(RollModeIsCondition), "rollModeIs")]
    [JsonDerivedType(typeof(HostKeyHeldCondition), "hostKeyHeld")]
    [JsonDerivedType(typeof(CombatActiveCondition), "combatActive")]
    [JsonDerivedType(typeof(RollLabelContainsCondition), "rollLabelContains")]
    [JsonDerivedType(typeof(AllOfCondition), "allOf")]
    [JsonDerivedType(typeof(AnyOfCondition), "anyOf")]
    [JsonDerivedType(typeof(NotCondition), "not")]
    public abstract record LoadedDiceCondition
    {
        public abstract bool Matches(LoadedDiceContext ctx);
    }

    public sealed record CurrentMapCondition(Guid MapId) : LoadedDiceCondition
    {
        public override bool Matches(LoadedDiceContext ctx)
            => ctx.State.ActiveMapId is Guid id && id == MapId;
    }

    public sealed record DiceTypeRolledCondition(int Sides) : LoadedDiceCondition
    {
        public override bool Matches(LoadedDiceContext ctx) => ctx.DiceTermSides == Sides;
    }

    // Matches when the sheet referenced by the roll is the named one. The
    // GmTarget sentinel matches rolls without any sheet attribution (raw
    // dice-tray rolls), mirroring how target lists treat "GM".
    public sealed record RollerIsCondition(Guid SheetId) : LoadedDiceCondition
    {
        public override bool Matches(LoadedDiceContext ctx)
            => SheetId == LoadedDiceRule.GmTarget
                ? ctx.RollerSheetId is null
                : ctx.RollerSheetId is Guid id && id == SheetId;
    }

    public sealed record RollModeIsCondition(RollMode Mode) : LoadedDiceCondition
    {
        public override bool Matches(LoadedDiceContext ctx) => ctx.Request.Mode == Mode;
    }

    // Logical key name as reported by KeyboardEvent.key on the host's client
    // (e.g. "Space", "Shift", "a"). Case-sensitive match against the live
    // snapshot pushed via UpdateHostInputStateAsync.
    public sealed record HostKeyHeldCondition(string Key) : LoadedDiceCondition
    {
        public override bool Matches(LoadedDiceContext ctx)
            => !string.IsNullOrEmpty(Key) && ctx.HostHeldKeys.Contains(Key);
    }

    public sealed record CombatActiveCondition : LoadedDiceCondition
    {
        public override bool Matches(LoadedDiceContext ctx) => ctx.State.ActiveCombat is not null;
    }

    public sealed record RollLabelContainsCondition(string Substring) : LoadedDiceCondition
    {
        public override bool Matches(LoadedDiceContext ctx)
            => !string.IsNullOrEmpty(Substring)
               && (ctx.Request.Label ?? string.Empty)
                   .Contains(Substring, StringComparison.OrdinalIgnoreCase);
    }

    // Vacuous AND ⇒ true. Matches the outer Conditions list's existing
    // "empty list = fires on every roll" convention so an empty group reads
    // the same as no group at all.
    public sealed record AllOfCondition(ImmutableArray<LoadedDiceCondition> Children) : LoadedDiceCondition
    {
        public override bool Matches(LoadedDiceContext ctx)
        {
            foreach (var child in Children)
                if (!child.Matches(ctx)) return false;
            return true;
        }
    }

    // Vacuous OR ⇒ false. An empty ANY-OF group reads as "never", which is
    // useful as a hard off-switch when authoring a rule incrementally.
    public sealed record AnyOfCondition(ImmutableArray<LoadedDiceCondition> Children) : LoadedDiceCondition
    {
        public override bool Matches(LoadedDiceContext ctx)
        {
            foreach (var child in Children)
                if (child.Matches(ctx)) return true;
            return false;
        }
    }

    // Null inner ⇒ matches. Lets the editor surface a placeholder NOT node
    // (freshly added, no child yet) without it accidentally firing the
    // rule until the host fills it in.
    public sealed record NotCondition(LoadedDiceCondition? Inner) : LoadedDiceCondition
    {
        public override bool Matches(LoadedDiceContext ctx)
            => Inner is null || !Inner.Matches(ctx);
    }
}
