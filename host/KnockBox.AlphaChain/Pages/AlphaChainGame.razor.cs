using KnockBox.AlphaChain.Components;
using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards;
using KnockBox.AlphaChain.Services.Logic.Games.FSM;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Services.State.Games.Data;
using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Shared;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KnockBox.AlphaChain.Pages
{
    public partial class AlphaChainGame : DisposableComponent
    {
        [Inject] protected AlphaChainGameEngine GameEngine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [Inject] protected ITickService TickService { get; set; } = default!;
        [Inject] protected IJSRuntime JS { get; set; } = default!;
        [Inject] protected ILogger<AlphaChainGame> Logger { get; set; } = default!;

        [Parameter] public AlphaChainGameState GameState { get; set; } = default!;

        private IDisposable? _stateSubscription;
        private IDisposable? _tickSubscription;

        // ── Client-owned word input (see wwwroot/js/alpha-chain-input.js) ────
        // The <input> is NOT value-bound to Blazor: the constant tick/state re-renders
        // would otherwise race fast typing over the circuit and clobber/flicker it. The
        // live DOM value is read via JS only at submit time (Enter / timeout auto-submit).
        private readonly string _inputId = $"ac-input-{Guid.NewGuid():N}";
        private ElementReference _wordInputRef;
        private IJSObjectReference? _inputModule;
        private DotNetObjectReference<AlphaChainGame>? _dotNetRef;
        private bool _inputRegistered;
        private string? _lastArmSig;

        /// <summary>Lead time (ms) the client auto-submits before the server shot-clock
        /// deadline, so typed-but-not-Entered text is sent rather than discarded.</summary>
        private const int AutoSubmitLeadMs = 400;

        /// <summary>Server-side mirror of the draft (committed on blur). The authoritative
        /// submit value comes live from JS; this is a resilience fallback.</summary>
        protected string WordInput { get; set; } = string.Empty;

        /// <summary>True while a submission is in flight, to debounce double-sends.</summary>
        protected bool IsSubmitting { get; private set; }

        /// <summary>The outcome of the local player's last submission, for inline feedback.</summary>
        protected SubmitWordResult? LastResult { get; private set; }

        /// <summary>The local player's id, or null before user init.</summary>
        protected string? CurrentUserId => UserService.CurrentUser?.Id;

        /// <summary>Whether it is the local player's turn (input is enabled only then).</summary>
        protected bool IsMyTurn =>
            CurrentUserId is not null && GameState.TurnManager.CurrentPlayer == CurrentUserId;

        /// <summary>Display name of the active player, or a placeholder when unset.</summary>
        protected string CurrentPlayerName
        {
            get
            {
                var id = GameState.TurnManager.CurrentPlayer;
                if (id is not null && GameState.GamePlayers.TryGetValue(id, out var ps))
                    return ps.DisplayName;
                return "—";
            }
        }

        /// <summary>The required start letter as an upper-case string, or "Any" when free.</summary>
        protected string RequiredStartDisplay =>
            GameState.RequiredStartLetter is { } c ? char.ToUpperInvariant(c).ToString() : "Any";

        /// <summary>The banned letter as an upper-case string, or "—" when unset.</summary>
        protected string BannedLetterDisplay =>
            GameState.BannedLetter is { } c ? char.ToUpperInvariant(c).ToString() : "—";

        /// <summary>Total duration of the per-turn shot clock (for the countdown ring).</summary>
        protected TimeSpan ShotClockDuration => TimeSpan.FromSeconds(GameState.Settings.ShotClockSeconds);

        /// <summary>Whole seconds left on the shot clock (never negative).</summary>
        protected int SecondsRemaining
        {
            get
            {
                var remaining = GameState.PhaseEndTime - DateTimeOffset.UtcNow;
                return remaining > TimeSpan.Zero ? (int)Math.Ceiling(remaining.TotalSeconds) : 0;
            }
        }

        /// <summary>Players ordered for the live leaderboard (score desc, then name).</summary>
        protected IReadOnlyList<AlphaChainPlayerState> Leaderboard =>
            GameState.GamePlayers.Values
                .OrderByDescending(p => p.Score)
                .ThenBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>The play feed, newest first.</summary>
        protected IReadOnlyList<AlphaChainWordPlay> PlayFeed =>
            GameState.PlayLog.AsEnumerable().Reverse().ToList();

        /// <summary>Most recent accepted words, oldest→newest, for the chain trail.</summary>
        protected IReadOnlyList<AlphaChainWordPlay> ChainTrail =>
            GameState.PlayLog.Count <= 7
                ? GameState.PlayLog
                : GameState.PlayLog.Skip(GameState.PlayLog.Count - 7).ToList();

        /// <summary>The newest accepted play, used to flash a score-pop on the leaderboard.</summary>
        protected AlphaChainWordPlay? LatestPlay =>
            GameState.PlayLog.Count > 0 ? GameState.PlayLog[^1] : null;

        /// <summary>Changes whenever a new word lands, so the score-pop @key remounts and re-animates.</summary>
        protected int LatestPlayKey => GameState.PlayLog.Count;

        // ── Leaderboard rank-change tracking (view-only, for the ▲/▼ indicator) ──
        private List<string> _prevRankOrder = new();
        private readonly Dictionary<string, int> _rankMovement = new();

        /// <summary>Maps a player to a turn-order accent slot (1-based, wraps at 6).</summary>
        protected int AccentSlot(string userId)
        {
            int i = 0;
            foreach (var id in GameState.TurnManager.TurnOrder)
            {
                if (id == userId) return (i % 6) + 1;
                i++;
            }
            return (Math.Abs(userId.GetHashCode()) % 6) + 1;
        }

        /// <summary>"▲" if the player moved up since the last reorder, "▼" if down, else "".</summary>
        protected string RankArrow(string userId) =>
            _rankMovement.TryGetValue(userId, out var m) ? m < 0 ? "▲" : m > 0 ? "▼" : "" : "";

        // ── Intermission (M4) ───────────────────────────────────────────────

        /// <summary>Whole seconds left on the current Intermission sub-phase timer (never negative).</summary>
        protected int SubPhaseSecondsRemaining
        {
            get
            {
                var remaining = GameState.SubPhaseEndTime - DateTimeOffset.UtcNow;
                return remaining > TimeSpan.Zero ? (int)Math.Ceiling(remaining.TotalSeconds) : 0;
            }
        }

        /// <summary>The configured duration (seconds) of the current sub-phase, or 0 when unknown
        /// (Deal/Expansion are brief fixed dwells with no meaningful progress bar).</summary>
        protected int SubPhaseDurationSeconds => GameState.IntermissionPhase switch
        {
            IntermissionSubPhase.Optimization => GameState.Settings.IntermissionCardSelectSeconds,
            IntermissionSubPhase.SniperBan => GameState.Settings.SniperBanSeconds,
            _ => 0
        };

        /// <summary>0–1 fraction of the sub-phase timer remaining, or 1 when duration is unknown.</summary>
        protected double SubPhaseFraction =>
            SubPhaseDurationSeconds > 0
                ? Math.Clamp((double)SubPhaseSecondsRemaining / SubPhaseDurationSeconds, 0, 1)
                : 1;

        /// <summary>How many players have locked in their Optimization ordering.</summary>
        protected int OptimizationSubmittedCount =>
            GameState.OptimizationSubmissions.Values.Count(s => s.Submitted);

        /// <summary>How many players are optimizing this Intermission.</summary>
        protected int OptimizationTotalCount => GameState.OptimizationSubmissions.Count;

        /// <summary>Whether the local player has already locked in their ordering.</summary>
        protected bool HasSubmittedOptimization =>
            CurrentUserId is { } id
            && GameState.OptimizationSubmissions.TryGetValue(id, out var sub)
            && sub.Submitted;

        /// <summary>Whether the local player is the resolved Sniper Ban picker.</summary>
        protected bool IsSniperBanPicker =>
            CurrentUserId is not null && CurrentUserId == GameState.SniperBanUserId;

        /// <summary>Display name of the Sniper Ban picker, for the waiting message.</summary>
        protected string SniperBanPickerName =>
            GameState.SniperBanUserId is { } id && GameState.GamePlayers.TryGetValue(id, out var ps)
                ? ps.DisplayName
                : "the last-place player";

        /// <summary>The legal banned letters for the picker, filtered by the match's ban mode.</summary>
        protected IReadOnlyList<char> LegalBanLetters =>
            BanLetterPool.For(GameState.Settings.BanMode).ToCharArray();

        /// <summary>Commits the local player's Engine Bay ordering during Optimization.</summary>
        protected async Task SubmitOptimizationAsync(IReadOnlyList<string> cardIds)
        {
            if (CurrentUserId is not { } id) return;
            var result = await GameEngine.SubmitOptimizationAsync(id, cardIds, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogWarning("Submit optimization failed: {Error}", error.PublicMessage);
        }

        /// <summary>Picks the next era's banned letter (last-place player only).</summary>
        protected async Task SelectSniperBanAsync(char letter)
        {
            if (CurrentUserId is not { } id) return;
            var result = await GameEngine.SelectSniperBanAsync(id, letter, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogWarning("Select Sniper Ban failed: {Error}", error.PublicMessage);
        }

        // ── Cards (M3) ──────────────────────────────────────────────────────

        /// <summary>The local player's per-player state, or null before init / if spectating.</summary>
        protected AlphaChainPlayerState? MyPlayer =>
            CurrentUserId is { } id && GameState.GamePlayers.TryGetValue(id, out var ps) ? ps : null;

        /// <summary>Whether the local user is the room host (gates the debug "Grant Cards" button).</summary>
        protected bool IsHost => CurrentUserId is not null && CurrentUserId == GameState.Host.Id;

        /// <summary>Opponents still in play, for the opponent bay summaries and Time Thief targeting.</summary>
        protected IReadOnlyList<AlphaChainPlayerState> Opponents =>
            GameState.GamePlayers.Values
                .Where(p => p.UserId != CurrentUserId && !p.HasLeft)
                .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>Targetable opponents for the Time Thief picker (active opponents only).</summary>
        protected IReadOnlyList<ActionHand.ActionTarget> ActionTargets =>
            Opponents.Where(p => !p.IsEliminated)
                .Select(p => new ActionHand.ActionTarget(p.UserId, p.DisplayName))
                .ToList();

        /// <summary>Badge text for a queued Pivot/Amnesty, or null when none is pending.</summary>
        protected string? PendingActionBadge => MyPlayer?.PendingAction switch
        {
            ActionKind.Pivot => "Pivot pending",
            ActionKind.Amnesty => "Amnesty pending",
            _ => null
        };

        /// <summary>Re-orders the local player's Engine Bay via the engine command path.</summary>
        protected async Task ReorderBayAsync(IReadOnlyList<string> cardIds)
        {
            if (CurrentUserId is not { } id) return;
            var result = await GameEngine.ReorderEngineBayAsync(id, cardIds, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogWarning("Reorder Engine Bay failed: {Error}", error.PublicMessage);
        }

        /// <summary>Plays an action card for the local player.</summary>
        protected async Task PlayActionAsync(ActionHand.ActionPlayRequest request)
        {
            if (CurrentUserId is not { } id) return;
            var result = await GameEngine.PlayActionAsync(id, request.CardId, request.TargetUserId, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogWarning("Play action failed: {Error}", error.PublicMessage);
        }

        /// <summary>Host-only debug deal so the card pipeline can be exercised before M4.</summary>
        protected async Task GrantCardsAsync()
        {
            if (CurrentUserId is not { } id) return;
            var result = await GameEngine.GrantCardsAsync(id, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogWarning("Grant cards failed: {Error}", error.PublicMessage);
        }

        protected override void OnInitialized()
        {
            // The parent lobby (LobbyPageBase) also re-renders on state changes, but
            // subscribing here keeps this view self-contained and correct in isolation.
            _stateSubscription = GameState.StateChangedEventManager.Subscribe(
                async () => await InvokeAsync(StateHasChanged));

            // Re-render once per second so the shot-clock countdown ticks down even when
            // no state change is raised. Every circuit registers this (it is render-only,
            // not the engine driver — the engine Tick is gated to the host in the lobby).
            var tickResult = TickService.RegisterTickCallback(
                () => _ = InvokeAsync(StateHasChanged), tickInterval: TickService.TicksPerSecond);
            if (tickResult.TryGetSuccess(out var sub))
                _tickSubscription = sub;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender)
                _dotNetRef = DotNetObjectReference.Create(this);

            UpdateRankMovement();

            bool roundActive = GameState.Phase == AlphaChainGamePhase.Round;

            if (roundActive)
            {
                if (_inputModule is null)
                    _inputModule = await JS.InvokeAsync<IJSObjectReference>(
                        "import", "./_content/KnockBox.AlphaChain/js/alpha-chain-input.js");

                if (!_inputRegistered)
                {
                    await _inputModule.InvokeVoidAsync("register", _inputId, _wordInputRef, _dotNetRef);
                    _inputRegistered = true;
                    _lastArmSig = null; // force a fresh arm + focus
                }

                // Re-arm the auto-submit deadline + focus only when the turn (or my deadline)
                // changes — not on every tick re-render.
                var sig = IsMyTurn
                    ? $"me|{GameState.PhaseEndTime.UtcTicks}"
                    : $"other|{GameState.TurnManager.CurrentPlayer}";
                if (sig != _lastArmSig)
                {
                    _lastArmSig = sig;
                    if (IsMyTurn)
                    {
                        var remainingMs = Math.Max(0, (GameState.PhaseEndTime - DateTimeOffset.UtcNow).TotalMilliseconds);
                        await _inputModule.InvokeVoidAsync("armDeadline", _inputId, remainingMs, AutoSubmitLeadMs);
                        await _inputModule.InvokeVoidAsync("focus", _inputId);
                    }
                    else
                    {
                        await _inputModule.InvokeVoidAsync("armDeadline", _inputId, -1, AutoSubmitLeadMs);
                        await _inputModule.InvokeVoidAsync("clear", _inputId);
                    }
                }
            }
            else if (_inputRegistered && _inputModule is not null)
            {
                await _inputModule.InvokeVoidAsync("unregister", _inputId);
                _inputRegistered = false;
                _lastArmSig = null;
            }
        }

        /// <summary>Recomputes per-player rank movement when the leaderboard order changes.
        /// View-only; does not trigger a render (the next tick re-render surfaces the arrows).</summary>
        private void UpdateRankMovement()
        {
            var order = Leaderboard.Select(p => p.UserId).ToList();
            if (order.SequenceEqual(_prevRankOrder)) return;

            if (_prevRankOrder.Count > 0)
            {
                _rankMovement.Clear();
                for (int i = 0; i < order.Count; i++)
                {
                    int prev = _prevRankOrder.IndexOf(order[i]);
                    _rankMovement[order[i]] = prev < 0 ? 0 : Math.Sign(i - prev);
                }
            }
            _prevRankOrder = order;
        }

        // ── JS-invoked submit paths (live DOM value, never the stale mirror) ──

        /// <summary>Invoked from JS on Enter or when the client-side timeout fires.</summary>
        [JSInvokable]
        public async Task OnWordSubmitted(string value)
        {
            WordInput = value ?? string.Empty;
            await SubmitWordAsync(WordInput);
        }

        /// <summary>Invoked from JS on blur — commits the draft to the server mirror only
        /// (does NOT play the word; clicking a card mid-turn must not submit prematurely).</summary>
        [JSInvokable]
        public void OnDraftCommitted(string value) => WordInput = value ?? string.Empty;

        /// <summary>Submit-button path: read the live DOM value, then submit.</summary>
        protected async Task SubmitFromButtonAsync()
        {
            if (_inputModule is null) return;
            var value = await _inputModule.InvokeAsync<string>("getValue", _inputId);
            await SubmitWordAsync(value);
        }

        /// <summary>
        /// Submits <paramref name="word"/> for the local player. Debounced via
        /// <see cref="IsSubmitting"/>; the input is cleared (client-side) only when accepted.
        /// </summary>
        protected async Task SubmitWordAsync(string word)
        {
            if (IsSubmitting || !IsMyTurn) return;

            var userId = CurrentUserId;
            if (userId is null) return;

            if (string.IsNullOrWhiteSpace(word)) return;

            IsSubmitting = true;
            try
            {
                var result = await GameEngine.SubmitWordAsync(userId, word, GameState);
                if (result.TryGetSuccess(out var outcome))
                {
                    LastResult = outcome;
                    // Clear the input only on an accepted word.
                    if (outcome is SubmitWordResult.Accepted or SubmitWordResult.AcceptedZeroPointTax)
                    {
                        WordInput = string.Empty;
                        if (_inputModule is not null)
                            await _inputModule.InvokeVoidAsync("clear", _inputId);
                    }
                }
                else if (result.TryGetFailure(out var error))
                {
                    Logger.LogWarning("Submit word failed: {Error}", error.PublicMessage);
                }
            }
            finally
            {
                IsSubmitting = false;
            }
        }

        /// <summary>Human-readable inline feedback for <see cref="LastResult"/>.</summary>
        protected string? FeedbackMessage => LastResult switch
        {
            SubmitWordResult.Accepted a => $"+{a.Score}",
            SubmitWordResult.AcceptedZeroPointTax => "Zero-Point Tax — banned letter used (0 points)",
            SubmitWordResult.RejectedNotYourTurn => "It's not your turn.",
            SubmitWordResult.RejectedChainBroken c => $"Word must start with '{char.ToUpperInvariant(c.Required)}'.",
            SubmitWordResult.RejectedNotInDictionary => "Not a word in the dictionary.",
            SubmitWordResult.RejectedDuplicate => "That word has already been played.",
            SubmitWordResult.RejectedEmpty => "Enter a word.",
            _ => null
        };

        /// <summary>Whether the last result was a rejection (for feedback styling).</summary>
        protected bool LastResultIsRejection =>
            LastResult is not (null or SubmitWordResult.Accepted or SubmitWordResult.AcceptedZeroPointTax);

        public override void Dispose()
        {
            _tickSubscription?.Dispose();
            _stateSubscription?.Dispose();
            _ = DisposeInputModuleAsync();
            _dotNetRef?.Dispose();
            base.Dispose();
        }

        /// <summary>Best-effort JS teardown. The circuit may already be gone (JSDisconnected),
        /// in which case the element is gone too and the swallow is harmless.</summary>
        private async Task DisposeInputModuleAsync()
        {
            if (_inputModule is null) return;
            try
            {
                await _inputModule.InvokeVoidAsync("unregister", _inputId);
                await _inputModule.DisposeAsync();
            }
            catch
            {
                // Circuit disconnected; nothing to clean up client-side.
            }
        }
    }
}
