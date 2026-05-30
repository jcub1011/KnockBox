using KnockBox.Core.Services.State.Users;
using KnockBox.LinkedList.Services.Logic;
using KnockBox.LinkedList.Services.Logic.Games;
using KnockBox.LinkedList.Services.State.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.LinkedList.Pages
{
    public partial class LobbyPhase : ComponentBase
    {
        [Inject] protected LinkedListGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected WordPairSource WordPairSource { get; set; } = default!;

        [Inject] protected ILogger<LobbyPhase> Logger { get; set; } = default!;

        [Parameter] public LinkedListGameState GameState { get; set; } = default!;

        protected bool SettingsOpen { get; private set; }

        protected bool IsHost => UserService.CurrentUser?.Id == GameState.Host.Id;

        protected bool CanStart =>
            GameState.IsJoinable
            && GameState.Players.Length >= GameEngine.MinPlayerCount
            && GameState.Players.Length <= GameEngine.MaxPlayerCount;

        protected void ToggleSettings() => SettingsOpen = !SettingsOpen;

        // ── Settings setters — every mutation routes through UpdateSettings so it
        //    runs inside State.Execute (atomic + change notification). ──

        protected ScoringMode Mode
        {
            get => GameState.Settings.ScoringMode;
            set => UpdateSettings(s => s with { ScoringMode = value });
        }

        protected PlayerStructure Structure
        {
            get => GameState.Settings.PlayerStructure;
            set => UpdateSettings(s => s with { PlayerStructure = value });
        }

        protected int RejectionCap
        {
            get => GameState.Settings.RejectionCap;
            set => UpdateSettings(s => s with { RejectionCap = value < 0 ? 0 : value });
        }

        protected bool NoImmediateRepeat
        {
            get => GameState.Settings.NoImmediateRepeat;
            set => UpdateSettings(s => s with { NoImmediateRepeat = value });
        }

        protected bool HostPlaysGame
        {
            get => GameState.Settings.HostPlaysGame;
            set => UpdateSettings(s => s with { HostPlaysGame = value });
        }

        protected bool ParEnabled
        {
            get => GameState.Settings.Par is not null;
            set => UpdateSettings(s => s with { Par = value ? (s.Par ?? 10) : null });
        }

        protected int ParValue
        {
            get => GameState.Settings.Par ?? 10;
            set => UpdateSettings(s => s with { Par = value < 1 ? 1 : value });
        }

        // ── Round word / auditor setters — written directly onto state (not settings). ──

        protected string StartWord
        {
            get => GameState.StartWord;
            set => SetState(() => GameState.StartWord = (value ?? "").Trim().ToUpperInvariant());
        }

        protected string DestinationWord
        {
            get => GameState.DestinationWord;
            set => SetState(() => GameState.DestinationWord = (value ?? "").Trim().ToUpperInvariant());
        }

        protected string AuditorPlayerId
        {
            get => GameState.AuditorPlayerId;
            set => SetState(() => GameState.AuditorPlayerId = value ?? "");
        }

        /// <summary>Applies a curated pair by its index in <see cref="WordPairSource.Pairs"/>.</summary>
        protected void ApplyCuratedPair(int index)
        {
            if (index < 0 || index >= WordPairSource.Pairs.Length) return;
            var pair = WordPairSource.Pairs[index];
            SetState(() =>
            {
                GameState.StartWord = pair.Start.ToUpperInvariant();
                GameState.DestinationWord = pair.Destination.ToUpperInvariant();
            });
        }

        private void UpdateSettings(Func<LinkedListSettings, LinkedListSettings> mutate)
        {
            if (GameState.UpdateSettings(mutate).TryGetFailure(out var error))
                Logger.LogError("Failed to update Linked List settings: {Error}", error.PublicMessage);
        }

        private void SetState(Action mutate)
        {
            if (GameState.Execute(mutate).TryGetFailure(out var error))
                Logger.LogError("Failed to update Linked List lobby state: {Error}", error.PublicMessage);
        }

        protected void KickPlayer(string userId)
        {
            if (!IsHost || string.IsNullOrWhiteSpace(userId) || userId == GameState.Host.Id) return;

            var player = GameState.Players.FirstOrDefault(e => e.User.Id == userId);
            if (player.User is null) return;

            if (GameState.KickPlayer(UserService.CurrentUser!, player.User).TryGetFailure(out var error))
                Logger.LogWarning("Error kicking player: {Error}", error.PublicMessage);
        }

        protected async Task StartGame()
        {
            if (UserService.CurrentUser is null) return;
            var result = await GameEngine.StartAsync(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogError("Failed to start Linked List game: {Error}", error.PublicMessage);
        }
    }
}
