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

        /// <summary>Bodies that actually play — registered players plus the host when
        /// "Host plays the game" is on. This is what the start gate and the player-count
        /// readout measure against, so toggling host-plays moves the count immediately.</summary>
        protected int ParticipantCount => GameState.Participants.Length;

        protected bool CanStart =>
            GameState.IsJoinable
            && ParticipantCount >= GameEngine.MinPlayerCount
            && ParticipantCount <= GameEngine.MaxPlayerCount
            && (Structure != PlayerStructure.Groups || TeamsValidity().Ok);

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
            set
            {
                UpdateSettings(s => s with { PlayerStructure = value });
                // Seed a sensible default team layout the first time Groups is chosen.
                if (value == PlayerStructure.Groups && GameState.GroupAssignments.Count == 0)
                    AutoBalanceTeams();
            }
        }

        // ── Group assignment (Groups mode, §8.2) ─────────────────────────────

        protected IReadOnlyList<string> ParticipantIds =>
            [.. GameState.Participants.Select(p => p.User.Id)];

        /// <summary>The number of teams; at least 2 in Groups mode.</summary>
        protected int GroupCount => Math.Max(2, GameState.GroupAssignments.Count);

        /// <summary>Upper bound on teams so every team can still hold ≥ 2 players.</summary>
        protected int MaxGroupCount => Math.Max(2, ParticipantIds.Count / 2);

        /// <summary>Number-stepper binding for the team count. Setting it re-balances.</summary>
        protected int GroupCountInput
        {
            get => GroupCount;
            set => SetGroupCount(value);
        }

        protected void SetGroupCount(int count)
        {
            count = Math.Clamp(count, 2, MaxGroupCount);
            PersistAssignments(LinkedListGameEngine.AutoBalanceGroups(ParticipantIds, count));
        }

        protected void AutoBalanceTeams()
            => PersistAssignments(LinkedListGameEngine.AutoBalanceGroups(ParticipantIds, GroupCount));

        /// <summary>Reassigns a player to a different team, keeping the team count fixed.</summary>
        protected void AssignPlayerToGroup(string playerId, int groupIndex)
        {
            var teams = ReconcileTeams();
            foreach (var t in teams) t.Remove(playerId);
            if (groupIndex < 0 || groupIndex >= teams.Count) groupIndex = 0;
            teams[groupIndex].Add(playerId);
            PersistAssignments(teams);
        }

        protected static string GroupLabel(int index) => $"Group {(char)('A' + index)}";

        protected string DisplayNameOf(string playerId)
        {
            var entry = GameState.Participants.FirstOrDefault(e => e.User.Id == playerId);
            return entry.User is not null ? entry.DisplayName : "Player";
        }

        /// <summary>Whether the current team layout can legally start a Groups match.</summary>
        protected (bool Ok, string? Message) TeamsValidity()
        {
            var teams = ReconcileTeams();
            if (teams.Count < 2) return (false, "Need at least 2 groups.");
            if (teams.Any(t => t.Count < 2)) return (false, "Each group needs at least 2 players.");
            return (true, null);
        }

        /// <summary>Pure (no-persist) view of the teams, reconciled against the current
        /// roster: members who left are dropped and players with no team join the
        /// smallest one. Safe to call during render.</summary>
        protected List<List<string>> ReconcileTeams()
        {
            var ids = ParticipantIds.ToHashSet();
            var teams = GameState.GroupAssignments.Count > 0
                ? GameState.GroupAssignments.Select(t => new List<string>(t)).ToList()
                : LinkedListGameEngine.AutoBalanceGroups(ParticipantIds, 2);

            foreach (var t in teams) t.RemoveAll(id => !ids.Contains(id));
            while (teams.Count < 2) teams.Add([]);

            var assigned = teams.SelectMany(t => t).ToHashSet();
            foreach (var id in ParticipantIds)
            {
                if (assigned.Add(id))
                    teams.OrderBy(t => t.Count).First().Add(id);
            }
            return teams;
        }

        private void PersistAssignments(List<List<string>> teams)
            => SetState(() => GameState.GroupAssignments = teams);

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

        protected int RoundsPerMatch
        {
            get => GameState.Settings.RoundsPerMatch;
            set => UpdateSettings(s => s with { RoundsPerMatch = value < 1 ? 1 : value });
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

        /// <summary>Restores every host-configurable rule to its out-of-the-box value by
        /// replacing the settings record with a fresh <see cref="LinkedListSettings"/>.
        /// Round words, the chosen Auditor, and team assignments live on state (not the
        /// settings record), so they're left untouched. Routed through
        /// <see cref="UpdateSettings"/> so <c>HostIsParticipant</c> is reflected in the
        /// same critical section.</summary>
        protected void ResetSettings() => UpdateSettings(_ => new LinkedListSettings());

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

            // Lock in a roster-consistent team layout so the engine sees a valid,
            // fully-assigned set of groups.
            if (Structure == PlayerStructure.Groups)
                PersistAssignments(ReconcileTeams());

            var result = await GameEngine.StartAsync(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogError("Failed to start Linked List game: {Error}", error.PublicMessage);
        }
    }
}
