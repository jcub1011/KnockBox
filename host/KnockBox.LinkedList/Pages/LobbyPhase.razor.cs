using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Users;
using KnockBox.LinkedList.Services.Storage;
using KnockBox.LinkedList.Services.Logic;
using KnockBox.LinkedList.Services.Logic.Games;
using KnockBox.LinkedList.Services.State.Games;
using Microsoft.AspNetCore.Components;

namespace KnockBox.LinkedList.Pages
{
    public partial class LobbyPhase : ComponentBase, IAsyncDisposable
    {
        [Inject] protected LinkedListGameEngine GameEngine { get; set; } = default!;

        [Inject] protected IUserService UserService { get; set; } = default!;

        [Inject] protected LinkedListStorage Storage { get; set; } = default!;

        [Inject] protected ILogger<LobbyPhase> Logger { get; set; } = default!;

        [Parameter] public LinkedListGameState GameState { get; set; } = default!;

        protected bool SettingsOpen { get; private set; }

        private readonly CancellationTokenSource _cts = new();
        private Task? _saveTask;

        // True once the host has changed any setting locally. Blocks the initial localStorage
        // load from clobbering an in-flight edit if the load returns after the user interacted.
        private bool _userHasEdited;

        protected bool IsHost => UserService.CurrentUser?.Id == GameState.Host.Id;

        /// <summary>Registered players (never the host). The lobby keeps the host a
        /// spectator until they pick a start button, so this is the stable count both
        /// start gates and the player-count readout measure against.</summary>
        protected int PlayerCount => GameState.Players.Length;

        /// <summary>Gate for starting with the host as the display only (host not playing).
        /// Counts registered players alone.</summary>
        protected bool CanStart =>
            GameState.IsJoinable
            && PlayerCount >= GameEngine.MinPlayerCount
            && PlayerCount <= GameEngine.MaxPlayerCount
            && (Structure != PlayerStructure.Groups || TeamsValidity().Ok);

        /// <summary>Gate for starting with the host playing. Counts the host alongside the
        /// registered players, and (in Groups mode) validates teams with the host seated.</summary>
        protected bool CanStartAsPlayer =>
            GameState.IsJoinable
            && PlayerCount + 1 >= GameEngine.MinPlayerCount
            && PlayerCount + 1 <= GameEngine.MaxPlayerCount
            && (Structure != PlayerStructure.Groups || TeamsValidityIncludingHost().Ok);

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

        protected IReadOnlyList<Guid> ParticipantIds =>
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
        protected void AssignPlayerToGroup(Guid playerId, int groupIndex)
        {
            var teams = ReconcileTeams();
            foreach (var t in teams) t.Remove(playerId);
            if (groupIndex < 0 || groupIndex >= teams.Count) groupIndex = 0;
            teams[groupIndex].Add(playerId);
            PersistAssignments(teams);
        }

        protected static string GroupLabel(int index) => $"Group {(char)('A' + index)}";

        protected string DisplayNameOf(Guid playerId)
        {
            var entry = GameState.Participants.FirstOrDefault(e => e.User.Id == playerId);
            return entry.User is not null ? entry.DisplayName : "Player";
        }

        /// <summary>Whether the current team layout can legally start a Groups match.</summary>
        protected (bool Ok, string? Message) TeamsValidity() => ValidateTeams(ReconcileTeams());

        /// <summary>Team validity for the "start as player" gate: the host is dropped into
        /// the smallest team first, mirroring how <see cref="StartGameAsPlayer"/> seats them.</summary>
        protected (bool Ok, string? Message) TeamsValidityIncludingHost()
        {
            var teams = ReconcileTeams();
            teams.OrderBy(t => t.Count).First().Add(GameState.Host.Id);
            return ValidateTeams(teams);
        }

        private static (bool Ok, string? Message) ValidateTeams(List<List<Guid>> teams)
        {
            if (teams.Count < 2) return (false, "Need at least 2 groups.");
            if (teams.Any(t => t.Count < 2)) return (false, "Each group needs at least 2 players.");
            return (true, null);
        }

        /// <summary>Pure (no-persist) view of the teams, reconciled against the current
        /// roster: members who left are dropped and players with no team join the
        /// smallest one. Safe to call during render.</summary>
        protected List<List<Guid>> ReconcileTeams()
        {
            var ids = ParticipantIds.ToHashSet();
            var teams = GameState.GroupAssignments.Count > 0
                ? GameState.GroupAssignments.Select(t => new List<Guid>(t)).ToList()
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

        private void PersistAssignments(List<List<Guid>> teams)
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
            get => GameState.AuditorPlayerId == Guid.Empty ? "" : GameState.AuditorPlayerId.ToString();
            set => SetState(() => GameState.AuditorPlayerId = Guid.TryParse(value, out var g) ? g : Guid.Empty);
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
            _userHasEdited = true;
            if (GameState.UpdateSettings(mutate).TryGetFailure(out var error))
            {
                Logger.LogError("Failed to update Linked List settings: {Error}", error.PublicMessage);
                return;
            }
            PersistSettings();
        }

        private void SetState(Action mutate)
        {
            if (GameState.Execute(mutate).TryGetFailure(out var error))
                Logger.LogError("Failed to update Linked List lobby state: {Error}", error.PublicMessage);
        }

        protected void KickPlayer(Guid userId)
        {
            if (!IsHost || userId == Guid.Empty || userId == GameState.Host.Id) return;

            var player = GameState.Players.FirstOrDefault(e => e.User.Id == userId);
            if (player.User is null) return;

            if (GameState.KickPlayer(UserService.CurrentUser!, player.User).TryGetFailure(out var error))
                Logger.LogWarning("Error kicking player: {Error}", error.PublicMessage);
        }

        /// <summary>Starts the match with the host as the shared display only (not playing).</summary>
        protected Task StartGame() => StartGameInternal(hostPlays: false);

        /// <summary>Starts the match with the host seated as a participant. In Groups mode the
        /// host is reconciled into the smallest team by <see cref="ReconcileTeams"/>.</summary>
        protected Task StartGameAsPlayer() => StartGameInternal(hostPlays: true);

        private async Task StartGameInternal(bool hostPlays)
        {
            if (UserService.CurrentUser is null) return;

            // Settle whether the host plays before building the roster. Applied straight to
            // state (not via UpdateSettings) so this start-time choice isn't persisted to the
            // host's saved settings.
            if (GameState.UpdateSettings(s => s with { HostPlays = hostPlays }).TryGetFailure(out var settingsError))
            {
                Logger.LogError("Failed to set host-plays before start: {Error}", settingsError.PublicMessage);
                return;
            }

            // Lock in a roster-consistent team layout so the engine sees a valid,
            // fully-assigned set of groups. With the host now a participant, ReconcileTeams
            // drops them into the smallest team.
            if (Structure == PlayerStructure.Groups)
                PersistAssignments(ReconcileTeams());

            var result = await GameEngine.StartAsync(UserService.CurrentUser, GameState);
            if (result.TryGetFailure(out var error))
                Logger.LogError("Failed to start Linked List game: {Error}", error.PublicMessage);
        }

        // ── Settings persistence (host browser localStorage) ─────────────────

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            // localStorage needs JS interop, so the host's saved settings load here (not
            // OnInitialized, which also runs during prerender). Host-only — only the host
            // edits and persists these.
            if (firstRender && IsHost)
                await LoadSettingsAsync();
        }

        private async Task LoadSettingsAsync()
        {
            var savedResult = await Storage.Local.GetAsync<LinkedListSettings>("settings", "value", _cts.Token);
            // A failed or canceled read is a non-success result that simply falls through to
            // the built-in defaults. If the host already edited a setting while the load was in
            // flight, the user's edit wins — the saved snapshot would clobber it.
            if (savedResult.TryGetSuccess(out var saved) && saved is not null && !_userHasEdited)
            {
                // Host-plays is no longer a persisted toggle — it's decided by the start
                // button — so force it off here. This also stops a value saved by the old
                // checkbox from making the host show up as a participant in the lobby.
                saved = saved with { HostPlays = false };
                // Apply through GameState directly (not the local UpdateSettings) so the
                // load doesn't flip _userHasEdited or re-persist the just-loaded value.
                if (GameState.UpdateSettings(_ => saved).TryGetFailure(out var error))
                {
                    Logger.LogError("Failed to apply saved Linked List settings: {Error}", error.PublicMessage);
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

        private async Task SaveSettingsAsync(LinkedListSettings settings, Task? prior, CancellationToken ct)
        {
            if (prior is not null)
            {
                try { await prior; } catch { /* prior failure already logged */ }
            }
            var saveResult = await Storage.Local.SetAsync("settings", "value", settings, ct);
            // Cancellation is silently ignored; a genuine storage failure is logged.
            if (saveResult.TryGetFailure(out var saveError))
                Logger.LogError("Error saving Linked List settings: {Error}", saveError.InternalMessage);
        }

        public async ValueTask DisposeAsync()
        {
            // Flush the last pending save before tearing down so a change made right before
            // navigating away isn't lost. A dead circuit makes SetAsync return a failure
            // Result, which SaveSettingsAsync logs.
            if (_saveTask is not null)
            {
                try { await _saveTask; } catch { /* best-effort flush */ }
            }

            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
