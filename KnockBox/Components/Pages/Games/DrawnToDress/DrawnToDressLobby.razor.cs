using KnockBox.Components.Shared;
using KnockBox.Services.Logic.Games.DrawnToDress;
using KnockBox.Services.Navigation;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using KnockBox.Services.State.Games.Shared;
using KnockBox.Services.State.Users;
using Microsoft.AspNetCore.Components;
using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Components.Pages.Games.DrawnToDress
{
    public partial class DrawnToDressLobby : DisposableComponent
    {
        [Inject] protected DrawnToDressGameEngine GameEngine { get; set; } = default!;
        [Inject] protected IGameSessionService GameSessionService { get; set; } = default!;
        [Inject] protected INavigationService NavigationService { get; set; } = default!;
        [Inject] protected IUserService UserService { get; set; } = default!;
        [Inject] protected ILogger<DrawnToDressLobby> Logger { get; set; } = default!;

        [Parameter] public string ObfuscatedRoomCode { get; set; } = default!;

        private IDisposable? _stateSubscription;
        private PeriodicTimer? _timer;
        private KnockBox.Core.Components.Shared.SvgDrawingCanvas? _canvas;

        // Track drawing sub-round to clear stale feedback when the type changes
        private int _prevDrawingTypeIndex = -1;
        // Track outfit round to clear stale feedback when the round changes
        private int _prevOutfitRound = -1;

        // ------------------------------------------------------------------
        // Lifecycle
        // ------------------------------------------------------------------

        protected override async Task OnInitializedAsync()
        {
            if (UserService.CurrentUser is null)
                await UserService.InitializeCurrentUserAsync(ComponentDetached);

            if (!GameSessionService.TryGetCurrentSession(out var session))
            {
                Logger.LogWarning("User [{id}] navigated to drawn-to-dress room [{code}] without an active session.",
                    UserService.CurrentUser?.Id ?? "Unknown", ObfuscatedRoomCode);
                NavigationService.ToHome();
                return;
            }

            if (!TryExtractObfuscatedRoomCode(session.LobbyRegistration.Uri, out var roomCode))
            {
                Logger.LogError("Could not parse room code from session URI [{uri}].",
                    session.LobbyRegistration.Uri);
                NavigationService.ToHome();
                return;
            }

            if (roomCode.Trim() != ObfuscatedRoomCode)
            {
                Logger.LogError("Session room code [{code}] does not match route [{route}].",
                    roomCode, ObfuscatedRoomCode);
                NavigationService.ToHome();
                return;
            }

            GameState = (DrawnToDressGameState)session.LobbyRegistration.State;

            if (GameState.IsDisposed)
            {
                NavigationService.ToHome();
                return;
            }

            GameState.OnStateDisposed += HandleStateDisposed;
            _stateSubscription = GameState.StateChangedEventManager.Subscribe(async () =>
            {
                // Clear stale drawing feedback when the host advances to a new clothing type
                if (GameState.CurrentDrawingTypeIndex != _prevDrawingTypeIndex)
                {
                    _prevDrawingTypeIndex = GameState.CurrentDrawingTypeIndex;
                    DrawingFeedback = string.Empty;
                }

                // Clear stale building feedback when a new outfit round starts
                if (GameState.CurrentOutfitRound != _prevOutfitRound)
                {
                    _prevOutfitRound = GameState.CurrentOutfitRound;
                    BuildingFeedback = string.Empty;
                }

                await InvokeAsync(StateHasChanged);
            });

            ThemeInput = GameState.Settings.Theme ?? string.Empty;

            // Start the 1-second countdown/tick timer
            _timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            _ = RunTimerAsync();

            await base.OnInitializedAsync();
        }

        private async Task RunTimerAsync()
        {
            var timer = _timer;
            if (timer is null) return;
            try
            {
                while (await timer.WaitForNextTickAsync(ComponentDetached))
                {
                    try
                    {
                        // Auto-submit the canvas drawing when the drawing deadline is about to expire
                        if (GameState?.CurrentPhase == GamePhase.Drawing &&
                            GameState.PhaseDeadlineUtc.HasValue &&
                            GameState.PhaseDeadlineUtc.Value <= DateTimeOffset.UtcNow.AddSeconds(1) &&
                            MyDrawingsThisType < GameState.Settings.MaxItemsPerType)
                        {
                            await TryAutoSubmitDrawingAsync();
                        }

                        // Auto-submit fully-selected (but not yet submitted) pending votes when the voting deadline expires
                        if (GameState?.CurrentPhase == GamePhase.Voting &&
                            GameState.PhaseDeadlineUtc.HasValue &&
                            GameState.PhaseDeadlineUtc.Value <= DateTimeOffset.UtcNow.AddSeconds(1))
                        {
                            TryAutoSubmitPendingVotes();
                        }

                        // All clients update the countdown display; the FSM deadline check
                        // ensures the auto-advance fires exactly once regardless of which
                        // client reaches it first.
                        GameEngine.Tick(GameState, DateTimeOffset.UtcNow);
                        await InvokeAsync(StateHasChanged);
                    }
                    catch (ObjectDisposedException) { break; }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "Error ticking DrawnToDress game.");
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>Silently submits the current canvas content as a drawing when the timer is about to expire.</summary>
        private async Task TryAutoSubmitDrawingAsync()
        {
            if (_canvas is null || GameState is null) return;
            try
            {
                var svgData = await _canvas.GetStorableSvgContentAsync();
                if (!string.IsNullOrWhiteSpace(svgData))
                {
                    var result = GameEngine.SubmitDrawing(UserService.CurrentUser!, GameState, svgData);
                    if (result.IsSuccess)
                        await _canvas.ClearAsync();
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Auto-submit drawing failed.");
            }
        }

        /// <summary>
        /// Submits any fully-completed pending votes (all criteria selected) when the voting timer expires.
        /// </summary>
        private void TryAutoSubmitPendingVotes()
        {
            if (GameState is null) return;
            var matchupIds = _pendingVotes.Keys.ToList();
            foreach (var matchupId in matchupIds)
            {
                if (CanSubmitVote(matchupId))
                    SubmitVote(matchupId);
            }
        }

        protected override void OnAfterRender(bool firstRender)
        {
            if (GameState?.KickedPlayers.Contains(UserService.CurrentUser) == true)
                GameSessionService.LeaveCurrentSession(navigateHome: true);

            base.OnAfterRender(firstRender);
        }

        private void HandleStateDisposed()
        {
            InvokeAsync(() =>
            {
                GameSessionService.LeaveCurrentSession(navigateHome: false);
                NavigationService.ToHome();
            });
        }

        public override void Dispose()
        {
            base.Dispose();
            _timer?.Dispose();
            if (GameState is not null)
                GameState.OnStateDisposed -= HandleStateDisposed;
            _stateSubscription?.Dispose();
        }

        // ------------------------------------------------------------------
        // State
        // ------------------------------------------------------------------

        protected DrawnToDressGameState GameState { get; set; } = default!;

        protected bool IsHost => UserService.CurrentUser?.Id == GameState?.Host.Id;

        protected string ThemeInput { get; set; } = string.Empty;
        protected string DrawingFeedback { get; set; } = string.Empty;
        protected string BuildingFeedback { get; set; } = string.Empty;
        protected string CustomizationFeedback { get; set; } = string.Empty;
        protected string OutfitName { get; set; } = string.Empty;

        // Per-matchup pending votes: matchupId → (criterion → voteForA)
        private readonly Dictionary<Guid, Dictionary<VotingCriterion, bool>> _pendingVotes = new();

        protected Outfit? MyCurrentOutfit =>
            GameState?.GetPlayerOutfit(UserService.CurrentUser?.Id ?? "", GameState.CurrentOutfitRound);

        protected bool IsMyOutfitComplete => MyCurrentOutfit?.IsComplete == true;
        protected bool IsOutfitLocked => MyCurrentOutfit?.IsLocked == true;

        protected int MyDrawingsThisType =>
            GameState?.AllDrawings.Count(d =>
                d.CreatorId == UserService.CurrentUser?.Id &&
                d.Type == GameState.CurrentDrawingType) ?? 0;

        protected string NextDrawingType
        {
            get
            {
                if (GameState is null || GameState.IsLastDrawingType) return string.Empty;
                var types = GameState.Settings.ClothingTypes;
                return types[GameState.CurrentDrawingTypeIndex + 1].ToString();
            }
        }

        /// <summary>Returns the time remaining in the current timed phase, or null when no timer is active.</summary>
        protected TimeSpan? PhaseTimeRemaining
        {
            get
            {
                if (GameState?.PhaseDeadlineUtc is null) return null;
                var remaining = GameState.PhaseDeadlineUtc.Value - DateTimeOffset.UtcNow;
                return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
            }
        }

        protected static string FormatCountdown(TimeSpan? span)
        {
            if (span is null) return string.Empty;
            return $"{(int)span.Value.TotalMinutes}:{span.Value.Seconds:D2}";
        }

        /// <summary>Seconds remaining at which the countdown badge switches to the "urgent" (pulsing red) style.</summary>
        protected const int CountdownUrgentThresholdSeconds = 10;

        // ------------------------------------------------------------------
        // Lobby actions
        // ------------------------------------------------------------------

        protected void SetTheme()
        {
            if (GameState is null || !IsHost) return;
            GameState.Execute(() =>
            {
                GameState.Settings.Theme = string.IsNullOrWhiteSpace(ThemeInput) ? null : ThemeInput.Trim();
            });
        }

        protected async Task StartGame()
        {
            if (GameState is null || !IsHost) return;
            await GameEngine.StartAsync(UserService.CurrentUser!, GameState);
        }

        protected void PlayAgain()
        {
            if (GameState is null || !IsHost) return;
            var result = GameEngine.ResetToLobby(UserService.CurrentUser!, GameState);
            if (result.TryGetFailure(out var err))
                Logger.LogWarning("Play again failed: {msg}", err.PublicMessage);
        }

        // ------------------------------------------------------------------
        // Drawing phase
        // ------------------------------------------------------------------

        protected async Task SubmitDrawing()
        {
            if (_canvas is null || GameState is null) return;
            DrawingFeedback = string.Empty;

            var svgData = await _canvas.GetStorableSvgContentAsync();
            if (string.IsNullOrWhiteSpace(svgData))
            {
                DrawingFeedback = "Canvas is empty. Draw something first!";
                return;
            }

            var result = GameEngine.SubmitDrawing(UserService.CurrentUser!, GameState, svgData);
            if (result.IsSuccess)
            {
                DrawingFeedback = $"Drawing submitted! ({MyDrawingsThisType}/{GameState.Settings.MaxItemsPerType})";
                await _canvas.ClearAsync();
            }
            else if (result.TryGetFailure(out var err))
            {
                DrawingFeedback = err.PublicMessage;
            }
        }

        protected void AdvanceDrawingRound()
        {
            if (GameState is null || !IsHost) return;
            DrawingFeedback = string.Empty;
            GameEngine.AdvanceDrawingRound(UserService.CurrentUser!, GameState);
        }

        // ------------------------------------------------------------------
        // Outfit building phase
        // ------------------------------------------------------------------

        protected void ClaimItem(Guid itemId)
        {
            if (GameState is null) return;
            BuildingFeedback = string.Empty;
            var result = GameEngine.ClaimItem(UserService.CurrentUser!, GameState, itemId);
            if (result.IsFailure)
                BuildingFeedback = "That item was just claimed by another player. Please choose another.";
        }

        protected void ReturnItem(ClothingItem item, ClothingType type)
        {
            if (GameState is null) return;
            GameEngine.ReturnItem(UserService.CurrentUser!, GameState, item.Id, type);
        }

        protected void LockOutfit()
        {
            if (GameState is null) return;
            BuildingFeedback = string.Empty;
            var result = GameEngine.LockOutfit(UserService.CurrentUser!, GameState);
            if (result.TryGetFailure(out var err))
                BuildingFeedback = err.PublicMessage;
        }

        protected void EndOutfitBuilding()
        {
            if (GameState is null || !IsHost) return;
            GameEngine.EndOutfitBuilding(UserService.CurrentUser!, GameState);
        }

        // ------------------------------------------------------------------
        // Customization phase
        // ------------------------------------------------------------------

        protected void SubmitOutfit()
        {
            if (GameState is null) return;
            CustomizationFeedback = string.Empty;

            var result = GameEngine.SubmitOutfit(UserService.CurrentUser!, GameState, OutfitName);
            if (result.TryGetFailure(out var err))
                CustomizationFeedback = err.PublicMessage;
            else
                OutfitName = string.Empty;
        }

        protected void EndCustomizationPhase()
        {
            if (GameState is null || !IsHost) return;
            GameEngine.EndCustomizationPhase(UserService.CurrentUser!, GameState);
        }

        // ------------------------------------------------------------------
        // Voting phase
        // ------------------------------------------------------------------

        protected bool? GetVoteSelection(Guid matchupId, VotingCriterion criterion)
        {
            if (_pendingVotes.TryGetValue(matchupId, out var criterionVotes) &&
                criterionVotes.TryGetValue(criterion, out bool v))
                return v;
            return null;
        }

        protected void SetVote(Guid matchupId, VotingCriterion criterion, bool voteForA)
        {
            if (!_pendingVotes.ContainsKey(matchupId))
                _pendingVotes[matchupId] = new Dictionary<VotingCriterion, bool>();
            _pendingVotes[matchupId][criterion] = voteForA;
        }

        protected bool CanSubmitVote(Guid matchupId)
        {
            if (!_pendingVotes.TryGetValue(matchupId, out var votes)) return false;
            return GameState?.Settings.VotingCriteria.All(c => votes.ContainsKey(c)) == true;
        }

        protected void SubmitVote(Guid matchupId)
        {
            if (GameState is null || !_pendingVotes.TryGetValue(matchupId, out var votes)) return;
            GameEngine.CastVote(UserService.CurrentUser!, GameState, matchupId, votes);
            _pendingVotes.Remove(matchupId);
        }

        protected void FinalizeVotingRound()
        {
            if (GameState is null || !IsHost) return;
            GameEngine.FinalizeVotingRound(UserService.CurrentUser!, GameState);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static bool TryExtractObfuscatedRoomCode(string uri, [NotNullWhen(true)] out string? code)
        {
            code = null;
            var split = uri.Trim().Trim('/').Split('/');
            if (split.Length <= 0) return false;
            code = split[^1];
            return true;
        }
    }
}
