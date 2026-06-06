using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KnockBox.Core.Components.Shared;
using KnockBox.DndMapper.Helpers;
using KnockBox.DndMapper.Services.Logic;
using KnockBox.DndMapper.Services.Logic.Visibility;
using KnockBox.DndMapper.Services.State.Games;
using KnockBox.DndMapper.Services.State.Games.Data;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace KnockBox.DndMapper.Pages.Components
{
    public partial class DiceCanvas : DisposableComponent, IAsyncDisposable
    {
        [Parameter, EditorRequired] public DndMapperGameState State { get; set; } = default!;
        [Parameter] public Guid CurrentUserId { get; set; }
        [Parameter] public bool IsHost { get; set; }

        [Inject] protected IJSRuntime Js { get; set; } = default!;
        [Inject] protected IDiceAnimationTracker Tracker { get; set; } = default!;
        [Inject] protected ILogger<DiceCanvas> Logger { get; set; } = default!;

        private ElementReference _overlayRef;
        private IJSObjectReference? _module;
        private DotNetObjectReference<DiceCanvas>? _selfRef;
        private IDisposable? _stateSub;

        // Roll ids already animated (or attempted) for this circuit. Prevents
        // re-animating the entire history on first paint or after a tracker
        // tick.
        private readonly HashSet<Guid> _seen = new();

        // Most recent rollId we kicked off per dice-box-key. Used to detect
        // interrupts: when a new roll arrives for the same key while a
        // previous one is still animating, settle the previous one
        // immediately so the log doesn't permanently hide it. The key is
        // composite: "user:{id}" for user-attributed rolls and "token:{id}"
        // for token-bound rolls (e.g. NPC initiative). Distinct keys mean
        // distinct DiceBox instances in JS, which is what enables several
        // NPC rolls to animate concurrently instead of clobbering each
        // other under the host's user id.
        private readonly Dictionary<string, Guid> _activeByKey = new();

        // Token ids whose 3D dice scene has already been prewarmed for this
        // circuit. The bulk NPC initiative roll appends N RollResults in one
        // notification; without prewarming, each one's first ensureBox()
        // call does a heavy Three.js initialize() and the burst saturates
        // the main thread. We instead fire prewarmBox() per NPC as soon as
        // we observe combat in WaitingForRolls, so by the time the host
        // clicks "roll all" every box is a warm cache hit.
        private readonly HashSet<Guid> _prewarmed = new();

        protected override void OnInitialized()
        {
            _stateSub = State.StateChangedEventManager.Subscribe(OnStateChangedAsync);
            base.OnInitialized();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;
            try
            {
                _module = await Js.InvokeAsync<IJSObjectReference>(
                    "import", "./_content/KnockBox.DndMapper/js/dndMapperDiceBox.js");
                _selfRef = DotNetObjectReference.Create(this);
                // Seed _seen with everything currently in the log so we don't
                // animate the entire history on first paint after a reconnect.
                foreach (var roll in State.RollLog) _seen.Add(roll.Id);
                // Then try to animate any rolls that arrived between the first
                // notification and the JS module being loaded.
                await PrewarmNpcBoxesAsync();
                await ProcessNewRollsAsync();
            }
            catch (JSDisconnectedException) { }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to load dndMapperDiceBox.js");
            }
        }

        private async ValueTask OnStateChangedAsync()
        {
            // Mark new rolls as animating in the synchronous prelude so any
            // sibling subscriber (RollLogPanel, DndMapperDisplay) sees
            // IsAnimating=true the first time it paints them. Without this,
            // subscriber-order races let the panel render the result before
            // the 3D dice are even kicked off.
            foreach (var roll in VisibleNewRolls())
            {
                Tracker.MarkAnimating(roll.Id);
            }
            await InvokeAsync(async () =>
            {
                await PrewarmNpcBoxesAsync();
                await ProcessNewRollsAsync();
            });
        }

        // Queue prewarm for every unrolled NPC combatant whenever combat
        // sits in WaitingForRolls. We send the full batch in a single JS
        // round-trip; JS then serializes the heavy Three.js init through
        // its internal initChain in the background. Awaiting each token's
        // init from C# would block this circuit handler for the full warm
        // duration — long enough for the host to click "Roll All" mid-warm
        // and still hit the cold-init burst. The batch path returns to C#
        // immediately. Idempotent via _prewarmed.
        private async Task PrewarmNpcBoxesAsync()
        {
            if (_module is null) return;
            var combat = State.ActiveCombat;
            if (combat is null || combat.Phase != CombatPhase.WaitingForRolls) return;

            var batch = new List<PrewarmConfig>();
            var added = new List<Guid>();
            foreach (var entry in combat.TurnOrder)
            {
                if (entry.OwnerUserId is not null) continue;     // players
                if (entry.InitiativeRoll is not null) continue;  // already rolled
                if (!_prewarmed.Add(entry.TokenId)) continue;    // already warmed this circuit

                var color = DiceColorResolver.ResolveForToken(State, entry.TokenId);
                batch.Add(new PrewarmConfig(
                    BoxKey: "token:" + entry.TokenId.ToString("N"),
                    Color: color,
                    FontColor: TokenTextContrast.TextFillFor(color)));
                added.Add(entry.TokenId);
            }

            if (batch.Count == 0) return;

            try
            {
                await _module.InvokeVoidAsync("prewarmBoxes", _overlayRef, batch);
            }
            catch (JSDisconnectedException)
            {
                // Circuit gone — the lazy ensureBox path on the next roll
                // will rebuild as needed. Drop the guards so a fresh
                // circuit can retry.
                foreach (var id in added) _prewarmed.Remove(id);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Dice prewarmBoxes batch failed ({Count} tokens)", batch.Count);
                foreach (var id in added) _prewarmed.Remove(id);
            }
        }

        private sealed record PrewarmConfig(string BoxKey, string Color, string FontColor);

        private async Task ProcessNewRollsAsync()
        {
            if (_module is null) return;

            foreach (var roll in VisibleNewRolls())
            {
                _seen.Add(roll.Id);

                // Token-bound rolls (NPC initiative bulk-roll) get their own
                // dice-box keyed by the token id, so several NPC rolls can
                // animate side-by-side rather than the host's single box
                // clobbering itself. Color comes from the token's resolved
                // (sheet-aware) color instead of the host's gold.
                string boxKey;
                string color;
                if (roll.TokenId is Guid tokenId)
                {
                    boxKey = "token:" + tokenId.ToString("N");
                    color = DiceColorResolver.ResolveForToken(State, tokenId);
                }
                else
                {
                    boxKey = "user:" + roll.RollerUserId;
                    color = DiceColorResolver.Resolve(State, roll.RollerUserId);
                }
                var fontColor = TokenTextContrast.TextFillFor(color);
                var notation = DiceNotationBuilder.Build(roll);
                if (string.IsNullOrEmpty(notation)) continue;

                // Interrupt: if this dice-box already has a roll animating,
                // settle the previous one immediately so the log reveals it.
                if (_activeByKey.TryGetValue(boxKey, out var previousRollId)
                    && previousRollId != roll.Id)
                {
                    Tracker.MarkSettled(previousRollId);
                }

                _activeByKey[boxKey] = roll.Id;
                Tracker.MarkAnimating(roll.Id);

                try
                {
                    await _module.InvokeVoidAsync(
                        "rollFor",
                        _overlayRef,
                        boxKey,
                        color,
                        fontColor,
                        notation,
                        _selfRef,
                        roll.Id);
                }
                catch (JSDisconnectedException)
                {
                    Tracker.MarkSettled(roll.Id);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "Dice rollFor failed for roll {RollId}", roll.Id);
                    Tracker.MarkSettled(roll.Id);
                }
            }
        }

        private IEnumerable<RollResult> VisibleNewRolls()
        {
            var visible = RollLogVisibilityFilter.VisibleTo(
                State.RollLog, CurrentUserId, IsHost, State.Settings.RollsVisibleToPlayers);
            foreach (var roll in visible)
            {
                if (!_seen.Contains(roll.Id)) yield return roll;
            }
        }

        // JS calls back with the same composite key we passed to rollFor —
        // see boxKey construction above. JSInvokable binds positionally, so
        // the parameter name can be whatever's most readable here.
        [JSInvokable]
        public Task OnRollSettled(string boxKey, Guid rollId)
        {
            Tracker.MarkSettled(rollId);
            if (_activeByKey.TryGetValue(boxKey, out var current) && current == rollId)
            {
                _activeByKey.Remove(boxKey);
            }
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            _stateSub?.Dispose();
            _selfRef?.Dispose();
            base.Dispose();
        }

        async ValueTask IAsyncDisposable.DisposeAsync()
        {
            if (_module is not null)
            {
                try { await _module.InvokeVoidAsync("disposeAll"); }
                catch (JSDisconnectedException) { }
                catch (Exception) { /* best-effort */ }
                try { await _module.DisposeAsync(); }
                catch (JSDisconnectedException) { }
                _module = null;
            }
            Dispose();
        }
    }
}
