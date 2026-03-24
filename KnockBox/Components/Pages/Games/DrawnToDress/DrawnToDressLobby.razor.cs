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
        private KnockBox.Core.Components.Shared.SvgDrawingCanvas? _canvas;

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
            _stateSubscription = GameState.StateChangedEventManager.Subscribe(
                async () => await InvokeAsync(StateHasChanged));

            ThemeInput = GameState.Settings.Theme ?? string.Empty;

            await base.OnInitializedAsync();
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

        // ------------------------------------------------------------------
        // Drawing phase
        // ------------------------------------------------------------------

        protected async Task SubmitDrawing()
        {
            if (_canvas is null || GameState is null) return;
            DrawingFeedback = string.Empty;

            var svgData = await _canvas.GetSvgContentAsync();
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
