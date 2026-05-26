using System.Text.Json.Serialization;
using KnockBox.DndMapper.Models;

namespace KnockBox.DndMapper.Services.State.Games.Data.LoadedDice
{
    // Strategy base for "when X" filters that gate a rule. Concrete subtypes
    // are tagged with [JsonDerivedType] so System.Text.Json round-trips them
    // through LibrarySnapshot without a custom converter. Adding a new
    // condition: declare the record, register it on this attribute list, and
    // add its editor component to LoadedDiceUiRegistry.
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
    [JsonDerivedType(typeof(CurrentMapCondition), "currentMap")]
    [JsonDerivedType(typeof(DiceTypeRolledCondition), "diceTypeRolled")]
    [JsonDerivedType(typeof(RollerIsCondition), "rollerIs")]
    [JsonDerivedType(typeof(RollModeIsCondition), "rollModeIs")]
    [JsonDerivedType(typeof(HostKeyHeldCondition), "hostKeyHeld")]
    [JsonDerivedType(typeof(CombatActiveCondition), "combatActive")]
    [JsonDerivedType(typeof(RollLabelContainsCondition), "rollLabelContains")]
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
}
