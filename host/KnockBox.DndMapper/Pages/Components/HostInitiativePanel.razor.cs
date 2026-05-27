using System.Globalization;
using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Users;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.Logic;
using KnockBox.DndMapper.Services.Logic.Games;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class HostInitiativePanel : DisposableComponent
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;

        [Inject] protected DndMapperGameEngine Engine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [Inject] protected IDiceAnimationTracker Tracker { get; set; } = default!;

        private IDisposable? _stateSub;
        private Action? _trackerSub;

        private bool _npcPickerOpen;
        private readonly HashSet<Guid> _picked = new();
        private string? _error;

        private readonly Dictionary<Guid, string> _npcDrafts = new();

        private bool _addOpen;
        private string _addTokenId = string.Empty;
        private string _addRoll = string.Empty;

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(async () => await InvokeAsync(StateHasChanged));
            // Tracker flips when DiceCanvas marks/settles a roll. Re-render so
            // the gated initiative cell reveals once the 3D dice land.
            _trackerSub = () => _ = InvokeAsync(StateHasChanged);
            Tracker.Changed += _trackerSub;
            base.OnInitialized();
        }

        public override void Dispose()
        {
            _stateSub?.Dispose();
            if (_trackerSub is not null) Tracker.Changed -= _trackerSub;
            base.Dispose();
        }

        // True whenever the value should stay hidden behind a placeholder:
        // (a) DiceCanvas still has a roll animating for this token, or
        // (b) the host typed a value via SetNpc but the engine hasn't fired
        // the dice yet (PendingInitiative is staged, waiting for every NPC
        // to be set or for "Roll All Unset NPCs" to flush the batch).
        // Either way the panel renders "—" so the result isn't spoiled.
        private bool IsInitiativeAnimating(CombatantEntry entry) =>
            entry.PendingInitiative is not null
            || InitiativeAnimationGate.IsAnimatingFor(State.RollLog, Tracker, entry);

        private void OpenNpcPicker()
        {
            _npcPickerOpen = true;
            _picked.Clear();
            _error = null;
        }

        private void ClosePicker()
        {
            _npcPickerOpen = false;
            _error = null;
        }

        private void TogglePick(Guid tokenId, bool picked)
        {
            if (picked) _picked.Add(tokenId);
            else _picked.Remove(tokenId);
        }

        private List<Token> NpcCandidates()
        {
            if (State.ActiveMapId is not Guid mapId) return new();
            var map = State.Maps.FirstOrDefault(m => m.Id == mapId);
            if (map is null) return new();
            return [.. map.Tokens.Where(t => t.Type == TokenType.NPCToken)];
        }

        private void StartCombat()
        {
            _error = null;
            var user = UserService.CurrentUser;
            if (user is null) return;
            var result = Engine.StartInitiativeAsync(State, user, [.. _picked]);
            if (result.TryGetFailure(out var err))
            {
                _error = err.PublicMessage;
                return;
            }
            _npcPickerOpen = false;
        }

        private void ForcePlayerRoll(Guid combatantId)
        {
            _error = null;
            var user = UserService.CurrentUser;
            if (user is null) return;
            var result = Engine.ForceInitiativeRollAsync(State, user, combatantId);
            if (result.TryGetFailure(out var err)) _error = err.PublicMessage;
        }

        private void OnInitiativeAttributeChange(string? raw)
        {
            _error = null;
            var user = UserService.CurrentUser;
            if (user is null) return;
            var result = Engine.SetInitiativeAttributeAsync(State, user, raw);
            if (result.TryGetFailure(out var err)) _error = err.PublicMessage;
        }

        private void RollAllNpcs()
        {
            _error = null;
            var user = UserService.CurrentUser;
            if (user is null) return;
            var result = Engine.RollAllNpcInitiativeAsync(State, user);
            if (result.TryGetFailure(out var err)) _error = err.PublicMessage;
        }

        private bool AnyUnrolledNpc() =>
            State.ActiveCombat is { } c
            && c.Phase == CombatPhase.WaitingForRolls
            && c.TurnOrder.Any(e => e.OwnerUserId is null && e.InitiativeRoll is null);

        private void OnNpcDraftInput(Guid combatantId, string? value)
            => _npcDrafts[combatantId] = value ?? string.Empty;

        private void OnNpcDraftKey(KeyboardEventArgs e, Guid combatantId)
        {
            if (e.Key == "Enter") CommitNpcDraft(combatantId);
        }

        private void CommitNpcDraft(Guid combatantId)
        {
            if (!_npcDrafts.TryGetValue(combatantId, out var draft) || string.IsNullOrWhiteSpace(draft)) return;
            if (!int.TryParse(draft, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)) return;
            var user = UserService.CurrentUser;
            if (user is null) return;
            _error = null;
            var result = Engine.SetNpcInitiativeAsync(State, user, combatantId, n);
            if (result.TryGetFailure(out var err)) { _error = err.PublicMessage; return; }
            _npcDrafts.Remove(combatantId);
        }

        private void AdvanceTurn()
        {
            _error = null;
            var user = UserService.CurrentUser;
            if (user is null) return;
            var result = Engine.AdvanceTurnAsync(State, user);
            if (result.TryGetFailure(out var err)) _error = err.PublicMessage;
        }

        private void EndCombat()
        {
            _error = null;
            var user = UserService.CurrentUser;
            if (user is null) return;
            var result = Engine.EndCombatAsync(State, user);
            if (result.TryGetFailure(out var err)) _error = err.PublicMessage;
        }

        private void RemoveCombatant(Guid combatantId)
        {
            _error = null;
            var user = UserService.CurrentUser;
            if (user is null) return;
            var result = Engine.RemoveCombatantAsync(State, user, combatantId);
            if (result.TryGetFailure(out var err)) _error = err.PublicMessage;
        }

        private void AddCombatant()
        {
            _error = null;
            var user = UserService.CurrentUser;
            if (user is null) return;
            if (!Guid.TryParse(_addTokenId, out var tokenId)) { _error = "Pick a token."; return; }
            if (!int.TryParse(_addRoll, NumberStyles.Integer, CultureInfo.InvariantCulture, out var roll))
            {
                _error = "Enter an initiative roll.";
                return;
            }
            var result = Engine.AddCombatantAsync(State, user, tokenId, roll);
            if (result.TryGetFailure(out var err)) { _error = err.PublicMessage; return; }
            _addOpen = false;
            _addTokenId = string.Empty;
            _addRoll = string.Empty;
        }
    }
}
