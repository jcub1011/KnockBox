using System.Collections.Immutable;
using System.Globalization;
using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapper.Services.State.Games.Data.LoadedDice;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DndMapper.Pages.Components.LoadedDice
{
    public partial class LoadedDiceRulesPanel : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public bool Editable { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            base.OnInitialized();
        }

        public override void Dispose()
        {
            _stateSub?.Dispose();
            base.Dispose();
        }

        // ── Rule CRUD ──────────────────────────────────────────────────────

        private async Task AddRule()
        {
            if (UserService.CurrentUser is null) return;
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = $"Rule {State.LoadedDiceRules.Count + 1}",
                Enabled = true,
            };
            var result = Engine.AddLoadedDiceRuleAsync(State, UserService.CurrentUser, rule);
            if (result.TryGetFailure(out var err))
                await PushToast(err.PublicMessage);
        }

        private Task RemoveRule(LoadedDiceRule rule)
        {
            if (UserService.CurrentUser is null) return Task.CompletedTask;
            var result = Engine.RemoveLoadedDiceRuleAsync(State, UserService.CurrentUser, rule.Id);
            return result.TryGetFailure(out var err) ? PushToast(err.PublicMessage) : Task.CompletedTask;
        }

        private Task SetEnabled(LoadedDiceRule rule, bool enabled) => UpdateRule(rule with { Enabled = enabled });
        private Task SetName(LoadedDiceRule rule, string name) => UpdateRule(rule with { Name = name });

        // ── Target list ────────────────────────────────────────────────────

        // `sheetId` may be the GmTarget sentinel (Guid.Empty), which the
        // processor recognizes as "rolls without a sheet". A null reflects
        // the placeholder "+ Add target…" option — that one we ignore.
        private Task AddTarget(LoadedDiceRule rule, Guid? sheetId)
        {
            if (sheetId is not Guid id) return Task.CompletedTask;
            return UpdateRule(rule with { TargetSheetIds = rule.TargetSheetIds.Add(id) });
        }

        private Task RemoveTarget(LoadedDiceRule rule, Guid sheetId)
            => UpdateRule(rule with { TargetSheetIds = rule.TargetSheetIds.Remove(sheetId) });

        // ── Condition list ─────────────────────────────────────────────────

        private Task AddCondition(LoadedDiceRule rule, string? kind)
        {
            if (string.IsNullOrEmpty(kind)) return Task.CompletedTask;
            LoadedDiceCondition? created = kind switch
            {
                "currentMap" => new CurrentMapCondition(State.ActiveMapId ?? State.Maps.FirstOrDefault()?.Id ?? Guid.Empty),
                "diceTypeRolled" => new DiceTypeRolledCondition(20),
                "rollerIs" => new RollerIsCondition(State.Sheets.Keys.FirstOrDefault()),
                "rollModeIs" => new RollModeIsCondition(RollMode.Normal),
                "hostKeyHeld" => new HostKeyHeldCondition(""),
                "combatActive" => new CombatActiveCondition(),
                "rollLabelContains" => new RollLabelContainsCondition(""),
                _ => null,
            };
            return created is null ? Task.CompletedTask
                : UpdateRule(rule with { Conditions = rule.Conditions.Add(created) });
        }

        private Task RemoveCondition(LoadedDiceRule rule, int index)
            => UpdateRule(rule with { Conditions = rule.Conditions.RemoveAt(index) });

        private Task UpdateCondition(LoadedDiceRule rule, int index, LoadedDiceCondition next)
            => UpdateRule(rule with { Conditions = rule.Conditions.SetItem(index, next) });

        // ── Modification list ──────────────────────────────────────────────

        private Task AddModification(LoadedDiceRule rule, string? kind)
        {
            if (string.IsNullOrEmpty(kind)) return Task.CompletedTask;
            LoadedDiceModification? created = kind switch
            {
                "setResult" => new SetResultModification(1),
                "clampMax" => new ClampMaxModification(10),
                "clampMin" => new ClampMinModification(2),
                "biasLower" => new BiasLowerModification(1),
                "biasHigher" => new BiasHigherModification(1),
                "rerollOn" => new RerollOnModification(ImmutableHashSet.Create(1)),
                _ => null,
            };
            return created is null ? Task.CompletedTask
                : UpdateRule(rule with { Modifications = rule.Modifications.Add(created) });
        }

        private Task RemoveModification(LoadedDiceRule rule, int index)
            => UpdateRule(rule with { Modifications = rule.Modifications.RemoveAt(index) });

        private Task UpdateModification(LoadedDiceRule rule, int index, LoadedDiceModification next)
            => UpdateRule(rule with { Modifications = rule.Modifications.SetItem(index, next) });

        // ── Shared ─────────────────────────────────────────────────────────

        private async Task UpdateRule(LoadedDiceRule rule)
        {
            if (UserService.CurrentUser is null) return;
            var result = Engine.UpdateLoadedDiceRuleAsync(State, UserService.CurrentUser, rule);
            if (result.TryGetFailure(out var err)) await PushToast(err.PublicMessage);
        }

        private Task PushToast(string message)
            => Toasts is null ? Task.CompletedTask : Toasts.Push(message, DndMapperToastTone.Danger);

        private static Guid? ParseGuid(string? s)
        {
            // Empty string ⇒ the "+ Add target…" placeholder; treat as null
            // so AddTarget skips it. Otherwise parse — Guid.Empty is a valid
            // value here because it's the GM-target sentinel.
            if (string.IsNullOrEmpty(s)) return null;
            return Guid.TryParse(s, out var g) ? g : null;
        }

        private static int ParseInt(object? raw, int fallback)
        {
            if (raw is null) return fallback;
            var s = raw.ToString();
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private string SheetName(Guid sheetId)
        {
            if (sheetId == LoadedDiceRule.GmTarget) return "GM";
            return State.Sheets.TryGetValue(sheetId, out var s)
                ? SheetDisplayName(s)
                : "(deleted sheet)";
        }

        private static string SheetDisplayName(CharacterSheet s)
            => string.IsNullOrEmpty(s.CharacterName)
                ? "(unnamed)"
                : (s.OwnerUserId is null ? $"{s.CharacterName} (NPC)" : s.CharacterName);
    }
}
