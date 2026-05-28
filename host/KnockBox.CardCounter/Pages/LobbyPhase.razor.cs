using KnockBox.Tooling.Collections;
using KnockBox.CardCounter.Services.Logic.Games;
using KnockBox.CardCounter.Services.State.Games;
using KnockBox.Core.Services.State.Users;
using KnockBox.Core.Services.Storage.ClientStorage;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace KnockBox.CardCounter.Pages
{
    public partial class LobbyPhase : ComponentBase, IAsyncDisposable
    {
        [Inject] protected CardCounterGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected ILocalStorageService LocalStorage { get; set; } = default!;

        [Inject] protected ILogger<LobbyPhase> Logger { get; set; } = default!;

        [Parameter] public CardCounterGameState GameState { get; set; } = default!;

        protected bool SettingsOpen { get; private set; } = false;

        private readonly CancellationTokenSource _cts = new();
        private Task? _saveTask;

        // True once the host has changed any setting locally. Blocks the initial localStorage
        // load from clobbering an in-flight edit if the load returns after the user interacted.
        private bool _userHasEdited;

        protected void ToggleSettings() => SettingsOpen = !SettingsOpen;

        // Per-property setters route every mutation through UpdateSettings so the change is
        // atomic, notification fires once outside the lock, and the snapshot is persisted to
        // the host's browser localStorage.
        protected void SetDeckSize(int v) => UpdateSettings(s => s with { DeckSize = v });
        protected void SetNumberToOperatorRatio(float v) => UpdateSettings(s => s with { NumberToOperatorRatio = v });
        protected void SetAddSubToMulDivRatio(float v) => UpdateSettings(s => s with { AddSubToMulDivRatio = v });
        protected void SetActionsDealtPerRound(int v) => UpdateSettings(s => s with { ActionsDealtPerRound = v });
        protected void SetActionHandLimit(int v) => UpdateSettings(s => s with { ActionHandLimit = v });
        protected void SetTotalPassesPerPlayer(int v) => UpdateSettings(s => s with { TotalPassesPerPlayer = v });
        protected void SetMinShoeSize(int v) => UpdateSettings(s => s with { MinShoeSize = v });
        protected void SetMaxShoeSize(int v) => UpdateSettings(s => s with { MaxShoeSize = v });
        protected void SetPlayerTurnTimeoutMs(int v) => UpdateSettings(s => s with { PlayerTurnTimeoutMs = v });
        protected void SetBuyInTimeoutMs(int v) => UpdateSettings(s => s with { BuyInTimeoutMs = v });
        protected void SetRoundEndTimeoutMs(int v) => UpdateSettings(s => s with { RoundEndTimeoutMs = v });
        protected void SetFeelingLuckyChainTimeoutMs(int v) => UpdateSettings(s => s with { FeelingLuckyChainTimeoutMs = v });
        protected void SetMakeMyLuckTimeoutMs(int v) => UpdateSettings(s => s with { MakeMyLuckTimeoutMs = v });
        protected void SetNotMyMoneyTimeoutMs(int v) => UpdateSettings(s => s with { NotMyMoneyTimeoutMs = v });
        protected void SetSkimTimeoutMs(int v) => UpdateSettings(s => s with { SkimTimeoutMs = v });
        protected void SetWaitingForReactionTimeoutMs(int v) => UpdateSettings(s => s with { WaitingForReactionTimeoutMs = v });
        protected void SetEnableActionTimer(bool v) => UpdateSettings(s => s with { EnableActionTimer = v });
        protected void SetShowMakeMyMoneyOperator(bool v) => UpdateSettings(s => s with { ShowMakeMyMoneyOperator = v });
        protected void SetFlipWinCondition(bool v) => UpdateSettings(s => s with { FlipWinCondition = v });
        protected void SetActiveOperatorMode(bool v) => UpdateSettings(s => s with { ActiveOperatorMode = v });
        protected void SetFeelingLuckyWeight(int v) => UpdateSettings(s => s with { FeelingLuckyWeight = v });
        protected void SetMakeMyLuckWeight(int v) => UpdateSettings(s => s with { MakeMyLuckWeight = v });
        protected void SetSkimWeight(int v) => UpdateSettings(s => s with { SkimWeight = v });
        protected void SetBurnWeight(int v) => UpdateSettings(s => s with { BurnWeight = v });
        protected void SetTurnTheTableWeight(int v) => UpdateSettings(s => s with { TurnTheTableWeight = v });
        protected void SetCompdWeight(int v) => UpdateSettings(s => s with { CompdWeight = v });
        protected void SetNotMyMoneyWeight(int v) => UpdateSettings(s => s with { NotMyMoneyWeight = v });
        protected void SetLaunderWeight(int v) => UpdateSettings(s => s with { LaunderWeight = v });
        protected void SetTiltWeight(int v) => UpdateSettings(s => s with { TiltWeight = v });
        protected void SetHedgeYourBetWeight(int v) => UpdateSettings(s => s with { HedgeYourBetWeight = v });
        protected void SetLetItRideWeight(int v) => UpdateSettings(s => s with { LetItRideWeight = v });

        private void UpdateSettings(Func<CardCounterSettings, CardCounterSettings> mutate)
        {
            _userHasEdited = true;
            GameState.UpdateSettings(mutate);
            PersistSettings();
        }

        // Two-step confirm so an accidental click can't wipe the host's whole config.
        // First click arms; a second click within the window resets. Auto-disarms after ~3s.
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
            UpdateSettings(_ => new CardCounterSettings());
        }

        private async Task DisarmResetAfterDelay(int generation)
        {
            try { await Task.Delay(TimeSpan.FromSeconds(3), _cts.Token); }
            catch (OperationCanceledException) { return; }   // component disposed
            if (generation != _resetGeneration) return;      // superseded by a newer arm/reset
            _resetArmed = false;
            await InvokeAsync(StateHasChanged);
        }

        protected void KickPlayer(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                Logger.LogWarning("Unable to kick provided user as it is null/whitespace.");
                return;
            }

            if (GameState.Host.Id != UserService.CurrentUser?.Id)
            {
                Logger.LogWarning("You [{id}] cannot kick players as you are not the host.", UserService.CurrentUser?.Id);
                return;
            }

            if (UserService.CurrentUser?.Id == userId)
            {
                Logger.LogWarning("Unable to kick host [{id}] from game.", userId);
                return;
            }

            int index = GameState.Players.IndexOf(entry => entry.User.Id == userId);
            if (index < 0)
            {
                Logger.LogWarning("Unable to kick player [{id}] as they aren't in the lobby.", userId);
                return;
            }

            var result = GameState.KickPlayer(UserService.CurrentUser!, GameState.Players[index].User);
            if (result.TryGetFailure(out var error))
            {
                Logger.LogWarning("Error kicking player [{error}].", error.PublicMessage);
            }
        }

        protected async Task StartGame()
        {
            if (UserService.CurrentUser is null) return;
            var result = await GameEngine.StartAsync(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogError("Failed to start game: {Error}", error);
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
            try
            {
                var saved = await LocalStorage.GetAsync<CardCounterSettings>("card-counter", "settings", _cts.Token);
                // If the host already edited a setting while the load was in flight,
                // the user's edit wins — the saved snapshot would clobber it.
                if (saved is not null && !_userHasEdited)
                {
                    GameState.UpdateSettings(_ => saved);
                    StateHasChanged();
                }
            }
            catch (OperationCanceledException) { /* component disposed */ }
            catch (ObjectDisposedException) { /* circuit gone */ }
            catch (JSDisconnectedException) { /* circuit gone */ }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error loading Card Counter settings.");
            }
        }

        private void PersistSettings()
        {
            var snapshot = GameState.Settings;
            _saveTask = SaveSettingsAsync(snapshot, _saveTask, _cts.Token);
        }

        private async Task SaveSettingsAsync(CardCounterSettings settings, Task? prior, CancellationToken ct)
        {
            if (prior is not null)
            {
                try { await prior; } catch { /* prior failure already logged */ }
            }
            try
            {
                await LocalStorage.SetAsync("card-counter", "settings", settings, ct);
            }
            catch (OperationCanceledException) { /* component disposed */ }
            catch (ObjectDisposedException) { /* circuit gone */ }
            catch (JSDisconnectedException) { /* circuit gone */ }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error saving Card Counter settings.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            // Flush the last pending save before tearing down so a change made right before
            // navigating away isn't lost. A dead circuit makes SetAsync throw
            // JSDisconnectedException, which SaveSettingsAsync swallows.
            if (_saveTask is not null)
            {
                try { await _saveTask; } catch { /* best-effort flush */ }
            }

            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
