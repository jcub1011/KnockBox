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
using Microsoft.JSInterop;

namespace KnockBox.DndMapper.Pages.Components.LoadedDice
{
    public partial class LoadedDiceRulesPanel : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public bool Editable { get; set; }

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [Inject] protected IJSRuntime JSRuntime { get; set; } = default!;
        [CascadingParameter] public DndMapperToastService? Toasts { get; set; }

        private IDisposable? _stateSub;
        private IJSObjectReference? _hostInputModule;

        // Identifies which HostKeyHeldCondition slot is currently listening
        // for a key, so the button can render its "Press a key…" state. Only
        // one slot listens at a time — the JS module's captureResolver is
        // also single-shot, so multi-listen would race anyway.
        private Guid _listeningRuleId;
        private ImmutableArray<int> _listeningPath;
        private bool _isListening;

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            base.OnInitialized();
        }

        public override void Dispose()
        {
            _stateSub?.Dispose();
            // Best-effort: tell JS to drop any in-flight capture so its
            // promise resolves to null instead of leaking the listener
            // past circuit teardown. Fire-and-forget — Dispose can't await.
            if (_hostInputModule is not null)
            {
                if (_isListening)
                {
                    try { _ = _hostInputModule.InvokeVoidAsync("cancelCapture"); }
                    catch { /* circuit teardown */ }
                }
                try { _ = _hostInputModule.DisposeAsync(); }
                catch { /* circuit teardown */ }
            }
            base.Dispose();
        }

        // Click handler for the "Set key…" / "<key>" button on a
        // HostKeyHeldCondition slot. Imports the host-input module on
        // first use (the parent page imports the same module separately
        // for the held-keys stream — JS module caching makes the second
        // import free).
        private async Task BeginHostKeyCapture(LoadedDiceRule rule, ImmutableArray<int> path)
        {
            if (!Editable || _isListening) return;
            _listeningRuleId = rule.Id;
            _listeningPath = path;
            _isListening = true;
            StateHasChanged();
            try
            {
                _hostInputModule ??= await JSRuntime.InvokeAsync<IJSObjectReference>(
                    "import", "./_content/KnockBox.DndMapper/js/dndMapperHostInput.js");
                var key = await _hostInputModule.InvokeAsync<string?>("captureNext");
                if (string.IsNullOrEmpty(key)) return; // Esc/cancelled
                // Re-read the rule from state in case the host edited it
                // (or deleted it) while listening — UpdateAtPath builds the
                // new outer list from `rule.Conditions`, so a stale rule
                // would clobber concurrent edits.
                var current = State.LoadedDiceRules.FirstOrDefault(r => r.Id == rule.Id);
                if (current is null) return;
                await UpdateAtPath(current, path, new HostKeyHeldCondition(key));
            }
            catch (JSDisconnectedException) { /* circuit teardown */ }
            catch (Exception)
            {
                // Capture failure is non-fatal — host can click again.
            }
            finally
            {
                _isListening = false;
                StateHasChanged();
            }
        }

        private bool IsListeningAt(LoadedDiceRule rule, ImmutableArray<int> path)
        {
            if (!_isListening || rule.Id != _listeningRuleId) return false;
            if (_listeningPath.Length != path.Length) return false;
            for (int i = 0; i < path.Length; i++)
                if (_listeningPath[i] != path[i]) return false;
            return true;
        }

        // ── Rule CRUD ──────────────────────────────────────────────────────

        private async Task AddRule()
        {
            if (UserService.CurrentUser is null) return;
            var rule = new LoadedDiceRule
            {
                Id = Guid.NewGuid(),
                Name = $"Rule {State.LoadedDiceRules.Length + 1}",
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

        // ── Condition tree ─────────────────────────────────────────────────
        //
        // The rule's outer Conditions list is an implicit AND. Composite
        // conditions (AllOf/AnyOf/Not) can nest inside it to form arbitrary
        // boolean trees. The editor addresses tree nodes by *path* — a
        // sequence of child indices walked from the outer list — so
        // siblings can be replaced/removed without re-indexing on each edit.
        //
        // Path conventions:
        //   []           — the outer Conditions list itself (only valid as
        //                  an AddChildAtPath target, meaning "append to root").
        //   [i]          — outer Conditions[i].
        //   [i, j]       — Children[j] of the composite at outer[i].
        //   [..., 0]     — for a NotCondition, addresses its single Inner slot.

        private Task AddConditionAtPath(LoadedDiceRule rule, ImmutableArray<int> path, string? kind)
        {
            var created = CreateConditionByKind(kind);
            return created is null
                ? Task.CompletedTask
                : UpdateRule(rule with { Conditions = AddChildToOuter(rule.Conditions, path.AsSpan(), created) });
        }

        private Task UpdateAtPath(LoadedDiceRule rule, ImmutableArray<int> path, LoadedDiceCondition next)
            => UpdateRule(rule with { Conditions = MutateOuter(rule.Conditions, path.AsSpan(), next) });

        private Task RemoveAtPath(LoadedDiceRule rule, ImmutableArray<int> path)
            => UpdateRule(rule with { Conditions = MutateOuter(rule.Conditions, path.AsSpan(), null) });

        private Task ChangeCompositeOpAtPath(LoadedDiceRule rule, ImmutableArray<int> path, string? newOp)
        {
            if (path.IsEmpty || string.IsNullOrEmpty(newOp)) return Task.CompletedTask;
            var current = ReadAt(rule.Conditions, path.AsSpan());
            LoadedDiceCondition? swapped = (current, newOp) switch
            {
                (AllOfCondition all, "anyOf") => new AnyOfCondition(all.Children),
                (AnyOfCondition any, "allOf") => new AllOfCondition(any.Children),
                _ => null,
            };
            return swapped is null ? Task.CompletedTask : UpdateAtPath(rule, path, swapped);
        }

        private LoadedDiceCondition? CreateConditionByKind(string? kind) => kind switch
        {
            "currentMap" => new CurrentMapCondition(State.ActiveMapId ?? State.Maps.FirstOrDefault()?.Id ?? Guid.Empty),
            "diceTypeRolled" => new DiceTypeRolledCondition(20),
            "rollerIs" => new RollerIsCondition(State.Sheets.Keys.FirstOrDefault()),
            "rollModeIs" => new RollModeIsCondition(RollMode.Normal),
            "hostKeyHeld" => new HostKeyHeldCondition(""),
            "combatActive" => new CombatActiveCondition(),
            "rollLabelContains" => new RollLabelContainsCondition(""),
            "allOf" => new AllOfCondition(ImmutableArray<LoadedDiceCondition>.Empty),
            "anyOf" => new AnyOfCondition(ImmutableArray<LoadedDiceCondition>.Empty),
            "not" => new NotCondition(null),
            _ => null,
        };

        // ── Path-based tree mutation ───────────────────────────────────────

        // `next == null` removes the entry at `path`; otherwise replaces it.
        // Removing a NotCondition's single inner slot (path ending in [0])
        // sets Inner=null rather than deleting the NOT itself — the empty
        // NOT placeholder is a deliberate editor state.
        private static ImmutableArray<LoadedDiceCondition> MutateOuter(
            ImmutableArray<LoadedDiceCondition> list,
            ReadOnlySpan<int> path,
            LoadedDiceCondition? next)
        {
            if (path.Length == 0) return list; // outer list itself isn't a node
            int idx = path[0];
            if (idx < 0 || idx >= list.Length) return list;
            var rest = path[1..];
            if (rest.Length == 0)
                return next is null ? list.RemoveAt(idx) : list.SetItem(idx, next);
            var newChild = MutateInside(list[idx], rest, next);
            return list.SetItem(idx, newChild);
        }

        private static LoadedDiceCondition MutateInside(
            LoadedDiceCondition node,
            ReadOnlySpan<int> path,
            LoadedDiceCondition? next)
        {
            return node switch
            {
                AllOfCondition all => all with { Children = MutateOuter(all.Children, path, next) },
                AnyOfCondition any => any with { Children = MutateOuter(any.Children, path, next) },
                NotCondition not when path[0] == 0 && path.Length == 1
                    => not with { Inner = next },
                NotCondition not when path[0] == 0 && not.Inner is not null
                    => not with { Inner = MutateInside(not.Inner, path[1..], next) },
                _ => node,
            };
        }

        // Empty path ⇒ append to the outer list. Otherwise descend to the
        // composite at `path` and append `child` to its children (or, for a
        // NotCondition, set its Inner slot).
        private static ImmutableArray<LoadedDiceCondition> AddChildToOuter(
            ImmutableArray<LoadedDiceCondition> list,
            ReadOnlySpan<int> path,
            LoadedDiceCondition child)
        {
            if (path.Length == 0) return list.Add(child);
            int idx = path[0];
            if (idx < 0 || idx >= list.Length) return list;
            var rest = path[1..];
            var newChild = AddChildToInside(list[idx], rest, child);
            return list.SetItem(idx, newChild);
        }

        private static LoadedDiceCondition AddChildToInside(
            LoadedDiceCondition node,
            ReadOnlySpan<int> path,
            LoadedDiceCondition child)
        {
            if (path.Length == 0)
            {
                return node switch
                {
                    AllOfCondition all => all with { Children = all.Children.Add(child) },
                    AnyOfCondition any => any with { Children = any.Children.Add(child) },
                    NotCondition not => not with { Inner = child },
                    _ => node, // leaf — can't add a child
                };
            }
            int idx = path[0];
            var rest = path[1..];
            return node switch
            {
                AllOfCondition all when idx >= 0 && idx < all.Children.Length
                    => all with { Children = all.Children.SetItem(idx, AddChildToInside(all.Children[idx], rest, child)) },
                AnyOfCondition any when idx >= 0 && idx < any.Children.Length
                    => any with { Children = any.Children.SetItem(idx, AddChildToInside(any.Children[idx], rest, child)) },
                NotCondition not when idx == 0 && not.Inner is not null
                    => not with { Inner = AddChildToInside(not.Inner, rest, child) },
                _ => node,
            };
        }

        private static LoadedDiceCondition? ReadAt(
            ImmutableArray<LoadedDiceCondition> outer,
            ReadOnlySpan<int> path)
        {
            if (path.Length == 0) return null;
            int idx = path[0];
            if (idx < 0 || idx >= outer.Length) return null;
            LoadedDiceCondition? node = outer[idx];
            for (int p = 1; p < path.Length && node is not null; p++)
            {
                int i = path[p];
                node = node switch
                {
                    AllOfCondition all => i >= 0 && i < all.Children.Length ? all.Children[i] : null,
                    AnyOfCondition any => i >= 0 && i < any.Children.Length ? any.Children[i] : null,
                    NotCondition not => i == 0 ? not.Inner : null,
                    _ => null,
                };
            }
            return node;
        }

        // Stable key for Razor's @key on path-addressed nodes. Independent
        // of the node's value, so editing a leaf's data doesn't trigger
        // a subtree rebuild (which would steal focus from text inputs).
        private static string PathKey(LoadedDiceRule rule, ImmutableArray<int> path)
            => path.IsEmpty ? rule.Id.ToString() : $"{rule.Id}/{string.Join(",", path)}";

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
