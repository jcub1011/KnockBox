using KnockBox.DrawnToDress.Services.Logic.Games;
using KnockBox.DrawnToDress.Services.State.Games;
using KnockBox.DrawnToDress.Services.State.Games.Data;
using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Users;
using KnockBox.DrawnToDress.Services.Storage;
using Microsoft.AspNetCore.Components;

namespace KnockBox.DrawnToDress.Pages
{
    public partial class LobbyPhase : ComponentBase, IAsyncDisposable
    {
        [Inject] protected DrawnToDressGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected DrawnToDressStorage Storage { get; set; } = default!;

        [Inject] protected ILogger<LobbyPhase> Logger { get; set; } = default!;

        [Parameter] public DrawnToDressGameState GameState { get; set; } = default!;

        protected bool SettingsOpen { get; private set; } = false;

        private readonly CancellationTokenSource _cts = new();
        private Task? _saveTask;

        // True once the host has changed any setting locally. Blocks the initial localStorage
        // load from clobbering an in-flight edit if the load returns after the user interacted.
        private bool _userHasEdited;

        protected bool IsClothingTypeEnabled(ClothingType id)
            => GameState.Settings.ClothingTypes.Any(t => t.Id == id);

        protected void ToggleClothingType(ClothingType id)
        {
            UpdateSettings(s =>
            {
                if (s.ClothingTypes.Any(t => t.Id == id))
                {
                    return s with { ClothingTypes = s.ClothingTypes.Where(t => t.Id != id).ToList() };
                }

                // Clone from the static defaults so dimensions and anchors are always correct,
                // and insert at the canonical position so the order matches DefaultClothingTypes.
                var template = DrawnToDressSettings.DefaultClothingTypes.First(t => t.Id == id);
                var defaults = DrawnToDressSettings.DefaultClothingTypes;
                int targetIndex = -1;
                for (int i = 0; i < defaults.Count; i++)
                {
                    if (defaults[i].Id == id) { targetIndex = i; break; }
                }
                int insertIndex = 0;
                for (int i = 0; i < targetIndex; i++)
                {
                    if (s.ClothingTypes.Any(t => t.Id == defaults[i].Id))
                        insertIndex++;
                }
                var next = s.ClothingTypes.ToList();
                next.Insert(insertIndex, template with { });
                return s with { ClothingTypes = next };
            });
        }

        protected void ToggleSettings() => SettingsOpen = !SettingsOpen;

        // Per-property setters route every mutation through UpdateSettings so the change is
        // atomic, notification fires once outside the lock, and the snapshot is persisted to
        // the host's browser localStorage.
        protected void SetDrawingTimeSec(int v) => UpdateSettings(s => s with { DrawingTimeSec = v });
        protected void SetEnableTimer(bool v) => UpdateSettings(s => s with { EnableTimer = v });
        protected void SetThemeAnnouncementTimeSec(int v) => UpdateSettings(s => s with { ThemeAnnouncementTimeSec = v });
        protected void SetAllowSketchingDuringOutfitBuilding(bool v) => UpdateSettings(s => s with { AllowSketchingDuringOutfitBuilding = v });
        protected void SetShowDrawingsOnHostScreen(bool v) => UpdateSettings(s => s with { ShowDrawingsOnHostScreen = v });
        protected void SetThemeSource(ThemeSource v) => UpdateSettings(s => s with { ThemeSource = v });
        protected void SetThemeAnnouncement(ThemeAnnouncement v) => UpdateSettings(s => s with { ThemeAnnouncement = v });
        protected void SetOutfitBuildingTimeSec(int v) => UpdateSettings(s => s with { OutfitBuildingTimeSec = v });
        protected void SetOutfitCustomizationTimeSec(int v) => UpdateSettings(s => s with { OutfitCustomizationTimeSec = v });
        protected void SetAllowReuseOwnItems(bool v) => UpdateSettings(s => s with { AllowReuseOwnItems = v });
        protected void SetAllowSelectOwnDrawings(bool v) => UpdateSettings(s => s with { AllowSelectOwnDrawings = v });
        protected void SetRequireDistinctItemsPerSlot(bool v) => UpdateSettings(s => s with { RequireDistinctItemsPerSlot = v });
        protected void SetVotingTimeSec(int v) => UpdateSettings(s => s with { VotingTimeSec = v });
        protected void SetShowCreatorDuringVoting(bool v) => UpdateSettings(s => s with { ShowCreatorDuringVoting = v });
        protected void SetVotingRounds(int v) => UpdateSettings(s => s with { VotingRounds = v });
        protected void SetBonusPointsForCompleteOutfit(int v) => UpdateSettings(s => s with { BonusPointsForCompleteOutfit = v });
        protected void SetHostDisconnectTimeoutSec(int v) => UpdateSettings(s => s with { HostDisconnectTimeoutSec = v });

        private void UpdateSettings(Func<DrawnToDressSettings, DrawnToDressSettings> mutate)
        {
            if (UserService.CurrentUser?.Id != GameState.Host.Id) return;
            _userHasEdited = true;
            if (GameState.UpdateSettings(s => mutate(s).Normalize()).TryGetFailure(out var error))
            {
                Logger.LogError("Failed to update Drawn To Dress settings: {Error}", error.PublicMessage);
                return;
            }
            PersistSettings();
        }

        protected void KickPlayer(Guid userId)
        {
            if (userId == Guid.Empty) return;
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

            PlayerEntry? match = null;
            foreach (var candidate in GameState.Players)
            {
                if (candidate.User.Id == userId) { match = candidate; break; }
            }
            if (match is null)
            {
                Logger.LogWarning("Cannot kick player [{id}]: not found.", userId);
                return;
            }

            // Kicking is a lobby-level state operation (same pattern as CardCounter).
            var result = GameState.KickPlayer(UserService.CurrentUser, match.Value.User);
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

        protected static readonly (string Name, string Description, Func<DrawnToDressSettings, DrawnToDressSettings> Apply)[] Presets =
        [
            ("Quick Game", "Short timers, 1 outfit round",
                s => s with
                {
                    DrawingTimeSec = 60,
                    OutfitBuildingTimeSec = 60,
                    OutfitCustomizationTimeSec = 30,
                    VotingTimeSec = 30,
                    NumOutfitRounds = 1,
                    VotingRounds = 2,
                    AllowReuseOwnItems = true,
                }),
            ("Standard", "Default settings",
                s => s with
                {
                    DrawingTimeSec = 180,
                    OutfitBuildingTimeSec = 90,
                    OutfitCustomizationTimeSec = 60,
                    VotingTimeSec = 60,
                    NumOutfitRounds = 1,
                    VotingRounds = 3,
                    AllowReuseOwnItems = true,
                }),
            ("Full Experience", "Longer timers, 2 outfit rounds",
                s => s with
                {
                    DrawingTimeSec = 180,
                    OutfitBuildingTimeSec = 120,
                    OutfitCustomizationTimeSec = 90,
                    VotingTimeSec = 90,
                    NumOutfitRounds = 2,
                    VotingRounds = 4,
                    AllowReuseOwnItems = true,
                }),
            ("Creative Focus", "Extra drawing & customization time, sketching required",
                s => s with
                {
                    DrawingTimeSec = 300,
                    OutfitBuildingTimeSec = 120,
                    OutfitCustomizationTimeSec = 120,
                    VotingTimeSec = 60,
                    NumOutfitRounds = 1,
                    VotingRounds = 3,
                    SketchingRequired = true,
                    AllowReuseOwnItems = true,
                }),
        ];

        protected void ApplyPreset(int index)
        {
            if (index < 0 || index >= Presets.Length) return;
            UpdateSettings(Presets[index].Apply);
        }

        // Two-step confirm so an accidental click can't wipe the host's whole config.
        // First click arms; a second click within the window resets. Auto-disarms after ~3s.
        // UpdateSettings already applies Normalize(), so the fresh record is normalized too.
        private bool _resetArmed;
        private int _resetGeneration;

        protected void ResetToDefaults()
        {
            if (!_resetArmed)
            {
                _resetArmed = true;
                _ = DisarmResetAfterDelay(++_resetGeneration);
                return;
            }
            _resetArmed = false;
            _resetGeneration++;                              // invalidate any pending disarm
            UpdateSettings(_ => new DrawnToDressSettings());
        }

        private async Task DisarmResetAfterDelay(int generation)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(3), _cts.Token); }
            catch (OperationCanceledException) { return; }   // component disposed
            if (generation != _resetGeneration) return;      // superseded by a newer arm/reset
            _resetArmed = false;
            await InvokeAsync(StateHasChanged);
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            // localStorage needs JS interop, so the host's saved settings load here (not
            // OnInitialized, which also runs during prerender). Host-only — only the host
            // edits and persists these.
            if (firstRender && UserService.CurrentUser?.Id == GameState.Host.Id)
            {
                await LoadSettingsAsync();
            }
        }

        private async Task LoadSettingsAsync()
        {
            // A failed/canceled read falls through to the built-in defaults already on the state.
            var savedResult = await Storage.Local.GetAsync<DrawnToDressSettings>("settings", "value", _cts.Token);
            // If the host already edited a setting while the load was in flight,
            // the user's edit wins — the saved snapshot would clobber it.
            if (savedResult.TryGetSuccess(out var saved) && saved is not null && !_userHasEdited)
            {
                if (GameState.UpdateSettings(_ => saved.Normalize()).TryGetFailure(out var error))
                {
                    Logger.LogError("Failed to apply saved Drawn To Dress settings: {Error}", error.PublicMessage);
                    return;
                }
                StateHasChanged();
            }
        }

        private void PersistSettings()
        {
            var snapshot = GameState.Settings;
            _saveTask = SaveSettingsAsync(snapshot, _saveTask, _cts.Token);
        }

        private async Task SaveSettingsAsync(DrawnToDressSettings settings, Task? prior, CancellationToken ct)
        {
            if (prior is not null)
            {
                try { await prior; } catch { /* prior failure already logged */ }
            }
            var saveResult = await Storage.Local.SetAsync("settings", "value", settings, ct);
            if (saveResult.TryGetFailure(out var saveError))
                Logger.LogError("Error saving Drawn To Dress settings: {Error}", saveError.InternalMessage);
        }

        public async ValueTask DisposeAsync()
        {
            // Flush the last pending save before tearing down so a change made right before
            // navigating away isn't lost. A dead circuit makes SetAsync return a failed Result,
            // which SaveSettingsAsync logs and swallows.
            if (_saveTask is not null)
            {
                try { await _saveTask; } catch { /* best-effort flush */ }
            }

            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
