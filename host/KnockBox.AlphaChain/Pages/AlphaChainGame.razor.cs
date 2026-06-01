using KnockBox.AlphaChain.Services.Logic.Games;
using KnockBox.AlphaChain.Services.Logic.Games.FSM;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Services.State.Games.Data;
using KnockBox.Core.Components.Shared;
using KnockBox.Core.Services.State.Shared;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.AlphaChain.Pages
{
    public partial class AlphaChainGame : DisposableComponent
    {
        [Inject] protected AlphaChainGameEngine GameEngine { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [Inject] protected ITickService TickService { get; set; } = default!;
        [Inject] protected ILogger<AlphaChainGame> Logger { get; set; } = default!;

        [Parameter] public AlphaChainGameState GameState { get; set; } = default!;

        private IDisposable? _stateSubscription;
        private IDisposable? _tickSubscription;

        /// <summary>The word the local player is typing. Bound to the text input.</summary>
        protected string WordInput { get; set; } = string.Empty;

        /// <summary>True while a submission is in flight, to debounce/disable the input.</summary>
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

        /// <summary>
        /// Submits <see cref="WordInput"/> for the local player. Debounced via
        /// <see cref="IsSubmitting"/>; the input is cleared only when the word is accepted.
        /// </summary>
        protected async Task SubmitWordAsync()
        {
            if (IsSubmitting || !IsMyTurn) return;

            var userId = CurrentUserId;
            if (userId is null) return;

            var word = WordInput;
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
                        WordInput = string.Empty;
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
            base.Dispose();
        }
    }
}
