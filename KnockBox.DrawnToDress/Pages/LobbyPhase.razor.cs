using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.DrawnToDress.Services.Logic.Games.FSM;
using KnockBox.DrawnToDress.Services.State.Games;
using KnockBox.DrawnToDress.Services.State.Games.Data;
using KnockBox.Core.Services.State.Users;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DrawnToDress.Pages
{
    public partial class LobbyPhase : ComponentBase
    {
        [Inject] protected DrawnToDressGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILogger<LobbyPhase> Logger { get; set; } = default!;

        [Parameter] public DrawnToDressGameState GameState { get; set; } = default!;

        protected bool SettingsOpen { get; private set; } = false;

        /// <summary>
        /// A local mutable copy of the config that the host edits in the settings panel.
        /// Kept in sync with <see cref="DrawnToDressGameState.Config"/> on first render and
        /// after each successful <see cref="ApplyConfig"/> call.
        /// </summary>
        protected DrawnToDressConfig EditConfig { get; private set; } = new();

        protected override void OnParametersSet()
        {
            // Refresh local edit copy whenever the parent passes a new state.
            SyncEditConfig();
        }

        private void SyncEditConfig()
        {
            var src = GameState.Config;
            EditConfig = new DrawnToDressConfig
            {
                DrawingTimeSec = src.DrawingTimeSec,
                ShowMannequin = src.ShowMannequin,
                EnableTimer = src.EnableTimer,
                AllowSketchingDuringOutfitBuilding = src.AllowSketchingDuringOutfitBuilding,
                ClothingTypes = src.ClothingTypes.Select(t => new ClothingTypeDefinition
                {
                    Id = t.Id,
                    DisplayName = t.DisplayName,
                    AllowMultiple = t.AllowMultiple,
                    CanvasWidth = t.CanvasWidth,
                    CanvasHeight = t.CanvasHeight,
                }).ToList(),
                ThemeSource = src.ThemeSource,
                ThemeAnnouncement = src.ThemeAnnouncement,
                ThemeAnnouncementTimeSec = src.ThemeAnnouncementTimeSec,
                OutfitBuildingTimeSec = src.OutfitBuildingTimeSec,
                OutfitCustomizationTimeSec = src.OutfitCustomizationTimeSec,
                AllowReuseOwnItems = src.AllowReuseOwnItems,
                RequireDistinctItemsPerSlot = src.RequireDistinctItemsPerSlot,
                NumOutfitRounds = src.NumOutfitRounds,
                CanReuseOutfit1Items = src.CanReuseOutfit1Items,
                Outfit2DistinctnessThreshold = src.Outfit2DistinctnessThreshold,
                SketchingRequired = src.SketchingRequired,
                VotingCriteria = src.VotingCriteria.Select(c => new VotingCriterionDefinition
                {
                    Id = c.Id,
                    DisplayName = c.DisplayName,
                    Weight = c.Weight,
                }).ToList(),
                VotingTimeSec = src.VotingTimeSec,
                ShowCreatorDuringVoting = src.ShowCreatorDuringVoting,
                VoteVisibility = src.VoteVisibility,
                VotingRounds = src.VotingRounds,
                BonusPointsForCompleteOutfit = src.BonusPointsForCompleteOutfit,
                RoundLeaderBonusPoints = src.RoundLeaderBonusPoints,
                TournamentWinnerBonusPoints = src.TournamentWinnerBonusPoints,
                VotingRoundResultsTimeSec = src.VotingRoundResultsTimeSec,
                CoinFlipTimeSec = src.CoinFlipTimeSec,
                HostDisconnectTimeoutSec = src.HostDisconnectTimeoutSec,
            };
        }

        protected void AddClothingType()
        {
            var id = $"custom_{Guid.NewGuid():N}";
            EditConfig.ClothingTypes.Add(new ClothingTypeDefinition
            {
                Id = id,
                DisplayName = "New Category",
                CanvasWidth = 600,
                CanvasHeight = 600,
            });
            ApplyConfig();
        }

        protected void RemoveClothingType(string id)
        {
            EditConfig.ClothingTypes.RemoveAll(t => t.Id == id);
            ApplyConfig();
        }

        protected void MoveClothingType(string id, int delta)
        {
            var idx = EditConfig.ClothingTypes.FindIndex(t => t.Id == id);
            if (idx == -1) return;

            int newIdx = idx + delta;
            if (newIdx < 0 || newIdx >= EditConfig.ClothingTypes.Count) return;

            var item = EditConfig.ClothingTypes[idx];
            EditConfig.ClothingTypes.RemoveAt(idx);
            EditConfig.ClothingTypes.Insert(newIdx, item);
            ApplyConfig();
        }

        protected void ToggleSettings() => SettingsOpen = !SettingsOpen;

        protected void ApplyConfig()
        {
            if (UserService.CurrentUser is null) return;
            if (GameState.Context is null) return;

            var cmd = new UpdateConfigCommand(UserService.CurrentUser.Id, EditConfig);
            var result = GameEngine.ProcessCommand(GameState.Context, cmd);
            if (result.TryGetFailure(out var err))
            {
                Logger.LogWarning("Failed to apply config: {msg}", err.PublicMessage);
                // Resync local copy from authoritative state.
                SyncEditConfig();
            }
        }

        protected void KickPlayer(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId)) return;
            if (GameState.Host.Id != UserService.CurrentUser?.Id)
            {
                Logger.LogWarning("Cannot kick: current user is not the host.");
                return;
            }
            if (userId == GameState.Host.Id)
            {
                Logger.LogWarning("Cannot kick the host.");
                return;
            }

            var player = GameState.Players.FirstOrDefault(p => p.Id == userId);
            if (player is null)
            {
                Logger.LogWarning("Cannot kick player [{id}]: not found.", userId);
                return;
            }

            // Kicking is a lobby-level state operation (same pattern as CardCounter).
            // It does not need to go through the FSM because it has no game-logic side-effects
            // while still in the lobby phase.
            var result = GameState.KickPlayer(player);
            if (result.TryGetFailure(out var err))
            {
                Logger.LogWarning("Error kicking player: {msg}", err.PublicMessage);
            }
        }

        protected async Task StartGame()
        {
            if (UserService.CurrentUser is null) return;
            var result = await GameEngine.StartAsync(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var err))
                Logger.LogError("Failed to start game: {msg}", err.PublicMessage);
        }

        // ── Presets ─────────────────────────────────────────────────────────────

        protected static readonly (string Name, string Description, Action<DrawnToDressConfig> Apply)[] Presets =
        [
            ("Quick Game", "Short timers, 1 outfit round",
                cfg =>
                {
                    cfg.DrawingTimeSec = 60;
                    cfg.OutfitBuildingTimeSec = 60;
                    cfg.OutfitCustomizationTimeSec = 30;
                    cfg.VotingTimeSec = 30;
                    cfg.NumOutfitRounds = 1;
                    cfg.VotingRounds = 2;
                    cfg.AllowReuseOwnItems = true;
                }),
            ("Standard", "Default settings",
                cfg =>
                {
                    cfg.DrawingTimeSec = 180;
                    cfg.OutfitBuildingTimeSec = 90;
                    cfg.OutfitCustomizationTimeSec = 60;
                    cfg.VotingTimeSec = 60;
                    cfg.NumOutfitRounds = 1;
                    cfg.VotingRounds = 3;
                    cfg.AllowReuseOwnItems = true;
                }),
            ("Full Experience", "Longer timers, 2 outfit rounds",
                cfg =>
                {
                    cfg.DrawingTimeSec = 180;
                    cfg.OutfitBuildingTimeSec = 120;
                    cfg.OutfitCustomizationTimeSec = 90;
                    cfg.VotingTimeSec = 90;
                    cfg.NumOutfitRounds = 2;
                    cfg.VotingRounds = 4;
                    cfg.AllowReuseOwnItems = true;
                }),
            ("Creative Focus", "Extra drawing & customization time, sketching required",
                cfg =>
                {
                    cfg.DrawingTimeSec = 300;
                    cfg.OutfitBuildingTimeSec = 120;
                    cfg.OutfitCustomizationTimeSec = 120;
                    cfg.VotingTimeSec = 60;
                    cfg.NumOutfitRounds = 1;
                    cfg.VotingRounds = 3;
                    cfg.SketchingRequired = true;
                    cfg.AllowReuseOwnItems = true;
                }),
        ];

        protected void ApplyPreset(int index)
        {
            if (index < 0 || index >= Presets.Length) return;
            Presets[index].Apply(EditConfig);
            ApplyConfig();
        }
    }
}

