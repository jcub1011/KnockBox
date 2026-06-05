using KnockBox.AlphaChain.Components;
using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.Logic.Games.FSM;
using KnockBox.AlphaChain.Services.Logic.Games.FSM.States;
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

        // ── Tutorial slide-away transition ──────────────────────────────────
        // When a tutorial ends, keep rendering it for a short window as a fixed overlay sliding
        // away on top of the now-rendered next phase, then drop it. Kept in sync with the CSS
        // animation duration in TutorialOverlay.razor.css.
        private const int TutorialExitMs = 450;
        private TutorialKind? _activeTutorial;
        private TutorialKind? _exitingTutorial;
        private CancellationTokenSource? _exitCts;

        /// <summary>The tutorial currently sliding away (rendered on top of the next phase), or null.</summary>
        protected TutorialKind? ExitingTutorial => _exitingTutorial;

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
        protected Guid? CurrentUserId => UserService.CurrentUser?.Id;

        /// <summary>Whether it is the local player's turn (input is enabled only then).</summary>
        protected bool IsMyTurn =>
            CurrentUserId is not null && GameState.TurnManager.CurrentPlayer == CurrentUserId;

        /// <summary>True while the round-ending word's score animation is playing and the FSM is
        /// holding before the Intermission/GameOver transition — all word entry is frozen.</summary>
        protected bool AwaitingTransition => GameState.PendingTransitionAt is not null;

        /// <summary>Status text shown in the entry while the end-of-round hold plays out.</summary>
        protected string TransitionHoldMessage =>
            GameState.PendingTransitionIsGameOver ? "final word — tallying…" : "era complete — tallying…";

        /// <summary>Whether the local player may submit right now (their turn and not mid-hold).</summary>
        protected bool CanSubmit => IsMyTurn && !AwaitingTransition;

        /// <summary>Display name of the active player, or a placeholder when unset.</summary>
        protected string CurrentPlayerName
        {
            get
            {
                var id = GameState.TurnManager.CurrentPlayer;
                if (id is { } pid && GameState.GamePlayers.TryGetValue(pid, out var ps))
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

        /// <summary>The personal hijack letter forced onto the local player (Tracer Round / Bait &amp;
        /// Switch) as an upper-case string, or null when none. Shown beside the banned letter so the
        /// cursed player can see it (it is personal — only the affected player sees their own).</summary>
        protected string? PersonalBannedLetterDisplay =>
            MyPlayer is { } me && RoomService<IHijackBanService>()?.Peek(me) is { } c
                ? char.ToUpperInvariant(c).ToString()
                : null;

        /// <summary>The local player's era-rolled personal banned letters (The Roulette Wheel, The Toll
        /// Booth) as distinct upper-case strings. Like <see cref="PersonalBannedLetterDisplay"/> these
        /// are personal — only the owner sees their own — and are shown beside the era ban in their own
        /// colour so the player understands why those letters tax them.</summary>
        protected IReadOnlyList<string> CardBannedLetterDisplays =>
            MyPlayer is { } me && RoomService<ICardBanService>() is { } bans
                ? bans.BansFor(me)
                    .Select(c => char.ToUpperInvariant(c).ToString())
                    .Distinct()
                    .OrderBy(s => s, StringComparer.Ordinal)
                    .ToList()
                : [];

        /// <summary>Resolves a room-scoped card-state service from the running game's context, or null
        /// before the game starts. Used for the personal/card ban displays above.</summary>
        private T? RoomService<T>() where T : class => GameState.Context?.EvaluationServices.Get<T>();

        /// <summary>A display-only evaluation context for <paramref name="player"/>'s Engine Bay,
        /// carrying the room state services so each card can render its own live badge (e.g. the
        /// Titanium Mirror's decayed "×0.7"). Null before the game starts.</summary>
        protected EngineEvaluationContext? BadgeContextFor(AlphaChainPlayerState player)
            => GameState.Context is { } context
                ? new EngineEvaluationContext(string.Empty, Array.Empty<char>(), new[] { player })
                {
                    Bay = player.EngineBay,
                    Services = context.EvaluationServices,
                    PlayerIndex = 0,
                }
                : null;

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

        /// <summary>The play feed (recent-words list), newest first.</summary>
        protected IReadOnlyList<AlphaChainSubmission> PlayFeed =>
            GameState.SubmissionHistory.AsEnumerable().Reverse().ToList();

        /// <summary>The word as shown in the recent-words list. Tunnel Vision masks the newest word's
        /// first &amp; last letter for its holder only — they must remember it (or read the required
        /// start letter); the chain rule stays server-enforced regardless.</summary>
        protected string DisplayWord(AlphaChainSubmission play, bool isNewest)
        {
            var word = play.Word.ToUpperInvariant();
            if (!isNewest || !LocalPlayerHasTunnelVision || word.Length == 0)
                return word;

            var chars = word.ToCharArray();
            chars[0] = '·';
            chars[^1] = '·';
            return new string(chars);
        }

        /// <summary>The latest accepted word's score replay, shown once in a fixed spot below the
        /// submit box for every player (the strip's subtitle names the submitter). Null when there
        /// is no play yet, or the last play had nothing to animate (no modifier cards walked and no
        /// Tax Collector steal to report).</summary>
        protected ScoreReplay? LatestReplay =>
            GameState.LatestScoreReplay is { HasAnimation: true } replay ? replay : null;

        /// <summary>The newest accepted play, used to flash a score-pop on the leaderboard.</summary>
        protected AlphaChainSubmission? LatestPlay =>
            GameState.SubmissionHistory.Count > 0 ? GameState.SubmissionHistory[^1] : null;

        /// <summary>Changes whenever a new word lands, so the score-pop @key remounts and re-animates.</summary>
        protected int LatestPlayKey => GameState.SubmissionHistory.Count;

        // ── Leaderboard rank-change tracking (view-only, for the ▲/▼ indicator) ──
        private List<Guid> _prevRankOrder = new();
        private readonly Dictionary<Guid, int> _rankMovement = new();

        // ── Mobile leaderboard auto-centre ──
        // The inline mobile strip scrolls to keep the local player's item centred; we re-centre
        // only when their rank index actually changes (not on every tick re-render).
        private ElementReference _mobileLbRef;
        private int _lastMyRankIndex = -1;

        /// <summary>Maps a player to a turn-order accent slot (1-based, wraps at 6).</summary>
        protected int AccentSlot(Guid userId)
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
        protected string RankArrow(Guid userId) =>
            _rankMovement.TryGetValue(userId, out var m) ? m < 0 ? "▲" : m > 0 ? "▼" : "" : "";

        // ── Intermission (M4) ───────────────────────────────────────────────

        /// <summary>The configured duration of the pre-round "Get Ready" countdown, feeding the
        /// shared client-side <c>CountdownClock</c> on the Countdown phase overlay.</summary>
        protected TimeSpan CountdownDuration => TimeSpan.FromSeconds(GameState.Settings.PreRoundCountdownSeconds);

        /// <summary>The configured duration of the current Intermission sub-phase, or
        /// <see cref="TimeSpan.Zero"/> when the sub-phase has no countdown. Feeds the shared
        /// client-side <c>CountdownClock</c> so the timer ticks smoothly off the circuit.</summary>
        protected TimeSpan SubPhaseDuration => GameState.IntermissionPhase switch
        {
            IntermissionSubPhase.Optimization => TimeSpan.FromSeconds(GameState.Settings.IntermissionCardSelectSeconds),
            IntermissionSubPhase.TaxTutorial => TutorialState.DurationFor(TutorialKind.Tax),
            IntermissionSubPhase.SniperBan => TimeSpan.FromSeconds(GameState.Settings.SniperBanSeconds),
            _ => TimeSpan.Zero
        };

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
            CurrentUserId is { } uid && uid == GameState.SniperBanUserId;

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

        /// <summary>Host-only: skips the currently-showing tutorial and advances immediately.</summary>
        protected async Task SkipTutorialAsync()
        {
            if (CurrentUserId is not { } id) return;
            var result = await GameEngine.SkipTutorialAsync(id, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogWarning("Skip tutorial failed: {Error}", error.PublicMessage);
        }

        // ── Cards (M3) ──────────────────────────────────────────────────────

        /// <summary>The local player's per-player state, or null before init / if spectating.</summary>
        protected AlphaChainPlayerState? MyPlayer =>
            CurrentUserId is { } id && GameState.GamePlayers.TryGetValue(id, out var ps) ? ps : null;

        /// <summary>Whether the local player holds Tunnel Vision — their view masks the first and last
        /// letter of the most recent chain word (owner-only; the chain rule is still server-enforced).</summary>
        protected bool LocalPlayerHasTunnelVision =>
            MyPlayer?.EngineBay.Any(c => c is IPreviousWordMask) == true;

        /// <summary>Whether the local player holds The Blindfold — their own word-input text is hidden
        /// while they type (a self-inflicted UI penalty traded for a multiplier; input still works).</summary>
        protected bool LocalPlayerHidesInput =>
            MyPlayer?.EngineBay.Any(c => c is IInputMask) == true;

        /// <summary>Whether the local user is the room host (gates the debug "Grant Cards" button).</summary>
        protected bool IsHost => CurrentUserId is { } uid && uid == GameState.Host.Id;

        /// <summary>Opponents still in play, for the opponent bay summaries and Time Thief targeting.</summary>
        protected IReadOnlyList<AlphaChainPlayerState> Opponents =>
            GameState.GamePlayers.Values
                .Where(p => p.UserId != CurrentUserId.GetValueOrDefault() && !p.HasLeft)
                .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

        protected override void OnInitialized()
        {
            // The parent lobby (LobbyPageBase) also re-renders on state changes, but
            // subscribing here keeps this view self-contained and correct in isolation.
            _stateSubscription = GameState.StateChangedEventManager.Subscribe(HandleStateChangedAsync);

            // Re-render once per second so the shot-clock countdown ticks down even when
            // no state change is raised. Every circuit registers this (it is render-only,
            // not the engine driver — the engine Tick is gated to the host in the lobby).
            var tickResult = TickService.RegisterTickCallback(
                () => _ = InvokeAsync(StateHasChanged), tickInterval: TickService.TicksPerSecond);
            if (tickResult.TryGetSuccess(out var sub))
                _tickSubscription = sub;
        }

        /// <summary>
        /// State-change handler. Detects a tutorial ending <b>before</b> re-rendering so the
        /// slide-away overlay is added in the same render that mounts the next phase (no flash),
        /// then renders.
        /// </summary>
        private async ValueTask HandleStateChangedAsync()
            => await InvokeAsync(() =>
            {
                DetectTutorialExit();
                StateHasChanged();
            });

        /// <summary>The tutorial currently on screen (full-screen phase or the Intermission Tax
        /// sub-phase), or null when no tutorial is showing.</summary>
        private TutorialKind? CurrentActiveTutorial()
        {
            if (GameState.Phase == AlphaChainGamePhase.Tutorial)
                return GameState.CurrentTutorial;
            if (GameState.Phase == AlphaChainGamePhase.Intermission
                && GameState.IntermissionPhase == IntermissionSubPhase.TaxTutorial)
                return TutorialKind.Tax;
            return null;
        }

        /// <summary>Tracks tutorial appearance/disappearance across state changes and kicks off the
        /// slide-away when one ends.</summary>
        private void DetectTutorialExit()
        {
            var current = CurrentActiveTutorial();
            if (current is { } showing)
            {
                // A tutorial is on screen — record it and cancel any pending exit (defensive; the
                // sequence never overlaps in practice).
                _activeTutorial = showing;
                _exitingTutorial = null;
            }
            else if (_activeTutorial is { } ended)
            {
                // A tutorial just left the screen — slide it away over the next phase.
                _activeTutorial = null;
                _exitingTutorial = ended;
                StartExitTimer();
            }
        }

        /// <summary>Removes the slide-away overlay once its animation has played.</summary>
        private void StartExitTimer()
        {
            _exitCts?.Cancel();
            _exitCts?.Dispose();
            _exitCts = new CancellationTokenSource();
            var token = _exitCts.Token;
            _ = RunExitTimerAsync(token);
        }

        private async Task RunExitTimerAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(TutorialExitMs, token);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            _exitingTutorial = null;
            await InvokeAsync(StateHasChanged);
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
                // changes — not on every tick re-render. The intermission hold disarms entry.
                var sig = AwaitingTransition
                    ? "hold"
                    : IsMyTurn
                        ? $"me|{GameState.PhaseEndTime.UtcTicks}"
                        : $"other|{GameState.TurnManager.CurrentPlayer}";
                if (sig != _lastArmSig)
                {
                    _lastArmSig = sig;
                    if (CanSubmit)
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

                // Re-centre the mobile leaderboard strip on the local player whenever their rank
                // changes (the strip is hidden on desktop, where centerMe is a no-op).
                if (CurrentUserId is { } myId)
                {
                    var board = Leaderboard;
                    int myIndex = -1;
                    for (int i = 0; i < board.Count; i++)
                    {
                        if (board[i].UserId == myId) { myIndex = i; break; }
                    }
                    if (myIndex >= 0 && myIndex != _lastMyRankIndex)
                    {
                        _lastMyRankIndex = myIndex;
                        await _inputModule.InvokeVoidAsync("centerMe", _mobileLbRef);
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

            if (CurrentUserId is not { } userId) return;

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

        /// <summary>Human-readable inline feedback for a rejected submission. Accepted plays
        /// (scored or taxed) produce no message here — the score-replay strip already shows the
        /// result — so the entry status row only surfaces errors.</summary>
        protected string? FeedbackMessage => LastResult switch
        {
            SubmitWordResult.RejectedNotYourTurn => "It's not your turn.",
            SubmitWordResult.RejectedChainBroken c => $"Word must start with '{char.ToUpperInvariant(c.Required)}'.",
            SubmitWordResult.RejectedNotInDictionary => "Not a word in the dictionary.",
            SubmitWordResult.RejectedDuplicate => "That word has already been played.",
            SubmitWordResult.RejectedEmpty => "Enter a word.",
            _ => null
        };

        /// <summary>Host-only: returns the match to the lobby so players can join/leave and
        /// settings can change before the next game. The engine clears all per-match state and
        /// flips back to joinable, which re-renders every player's page at the lobby.</summary>
        protected Task ReturnToLobbyAsync()
        {
            if (UserService.CurrentUser is not { } user) return Task.CompletedTask;
            var result = GameEngine.ReturnToLobby(user, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogError("Failed to return Alpha Chain to the lobby: {Error}", error);
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
            _exitCts?.Cancel();
            _exitCts?.Dispose();
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
