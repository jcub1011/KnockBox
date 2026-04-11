using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Extensions.Returns;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM.States
{
    /// <summary>
    /// Selects and (optionally) announces the theme for this drawing session.
    ///
    /// Supported theme sources:
    /// - <see cref="ThemeSource.Random"/>: theme is chosen on entry; transitions immediately
    ///   to <see cref="DrawingRoundState"/>.
    /// - <see cref="ThemeSource.HostPick"/>: waits for a <see cref="SelectThemeCommand"/>
    ///   from the host before advancing.
    /// - <see cref="ThemeSource.PlayerWritten"/>: each player submits a theme via
    ///   <see cref="SubmitPlayerThemeCommand"/>; once all players have submitted, one of the
    ///   submitted themes is chosen at random and the session advances.
    /// - <see cref="ThemeSource.RandomVoting"/>: a random subset of candidate themes is
    ///   presented; players vote via <see cref="VoteForThemeCommand"/>; the candidate with the
    ///   most votes wins (ties broken randomly); session advances once all players have voted.
    ///
    /// Announcement timing:
    /// - <see cref="ThemeAnnouncement.BeforeDrawing"/>: sets
    ///   <see cref="DrawnToDressGameState.ThemeRevealedToPlayers"/> immediately.
    /// - <see cref="ThemeAnnouncement.AfterDrawing"/>: the theme is persisted in
    ///   <see cref="DrawnToDressGameState.CurrentTheme"/> but the reveal flag is left
    ///   <see langword="false"/> until after drawing completes (see
    ///   <see cref="PoolRevealState"/>).
    ///
    /// Other commands:
    /// - <see cref="PauseGameCommand"/> (host only) → <see cref="PausedState"/>
    /// </summary>
    public sealed class ThemeSelectionState : ITimedDrawnToDressGameState
    {

        /// <summary>
        /// Placeholder theme list used when no themes have been configured.
        /// Later issues will populate this from a proper theme repository.
        /// </summary>
        private static readonly ThemeDefinition[] FallbackThemes =
        [
            new("retro_futurism", "Retro Futurism",
                "Outfits inspired by 1950s-style visions of the future."),
            new("enchanted_forest", "Enchanted Forest",
                "Mystical nature-themed outfits from a world of fairy tales."),
            new("street_style", "Street Style",
                "Urban casual wear with bold attitude."),
            new("underwater_world", "Underwater World",
                "Deep-sea creatures and ocean explorer fashion."),
            new("steampunk", "Steampunk",
                "Victorian-era mechanics meets fantasy engineering."),
        ];

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> OnEnter(
            DrawnToDressGameContext context)
        {
            context.State.SetPhase(GamePhase.ThemeSelection);
            context.Logger.LogInformation("FSM → ThemeSelectionState");

            context.State.PhaseDeadlineUtc = DateTimeOffset.UtcNow.AddSeconds(context.Config.ThemeAnnouncementTimeSec);

            switch (context.Config.ThemeSource)
            {
                case ThemeSource.Random:
                    SelectRandomTheme(context);
                    ApplyAnnouncementTiming(context);
                    context.Logger.LogInformation(
                        "Random theme selected: [{id}] \"{name}\".",
                        context.State.CurrentTheme?.Id, context.State.CurrentTheme?.DisplayName);
                    
                    return null;

                case ThemeSource.RandomVoting:
                    PopulateThemeCandidates(context);
                    context.Logger.LogInformation(
                        "RandomVoting: {count} candidate(s) presented.", context.State.ThemeCandidates.Count);
                    // Remain in this state until all players have voted.
                    return null;

                case ThemeSource.HostPick:
                case ThemeSource.PlayerWritten:
                default:
                    // Remain in this state waiting for player input.
                    return null;
            }
        }

        public Result OnExit(DrawnToDressGameContext context)
        {
            context.State.PhaseDeadlineUtc = null;
            return Result.Success;
        }

        public ValueResult<TimeSpan> GetRemainingTime(DrawnToDressGameContext context, DateTimeOffset now)
            => context.State.PhaseDeadlineUtc is { } deadline
                ? deadline - now
                : new ResultError("No timer active.");

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> Tick(
            DrawnToDressGameContext context, DateTimeOffset now)
        {
            // Only auto-advance if we have a selected theme and we're not waiting for votes/input.
            if (context.State.CurrentTheme != null && 
                (context.Config.ThemeSource == ThemeSource.Random || 
                 context.Config.ThemeSource == ThemeSource.RandomVoting && context.GamePlayers.Keys.All(id => context.State.ThemeVotes.ContainsKey(id)) ||
                 context.Config.ThemeSource == ThemeSource.PlayerWritten && context.GamePlayers.Keys.All(id => context.State.PlayerThemeSubmissions.ContainsKey(id)) ||
                 context.Config.ThemeSource == ThemeSource.HostPick))
            {
                if (context.State.PhaseDeadlineUtc is { } deadline && now >= deadline)
                {
                    return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(new DrawingRoundState());
                }
            }

            return null;
        }

        public ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleCommand(
            DrawnToDressGameContext context, DrawnToDressCommand command)
        {
            switch (command)
            {
                case SelectThemeCommand cmd:
                    return HandleSelectTheme(context, cmd);

                case SubmitPlayerThemeCommand cmd:
                    return HandleSubmitPlayerTheme(context, cmd);

                case VoteForThemeCommand cmd:
                    return HandleVoteForTheme(context, cmd);

                case PauseGameCommand:
                    return new PausedState(this);

                default:
                    context.Logger.LogWarning(
                        "ThemeSelectionState: unrecognized command [{type}] from player [{id}].",
                        command.GetType().Name, command.PlayerId);
                    return null;
            }
        }

        // ── Command handlers ──────────────────────────────────────────────────

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleSelectTheme(
            DrawnToDressGameContext context, SelectThemeCommand cmd)
        {
            if (context.Config.ThemeSource != ThemeSource.HostPick)
            {
                context.Logger.LogWarning(
                    "SelectTheme ignored: theme source is not HostPick.");
                return null;
            }

            if (cmd.PlayerId != context.State.Host.Id)
            {
                context.Logger.LogWarning(
                    "SelectTheme rejected: player [{id}] is not the host.", cmd.PlayerId);
                return null;
            }

            context.State.CurrentTheme = new ThemeDefinition(cmd.ThemeId, cmd.ThemeId);
            ApplyAnnouncementTiming(context);
            context.Logger.LogInformation("Host selected theme [{id}].", cmd.ThemeId);
            return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(new DrawingRoundState());
        }

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleSubmitPlayerTheme(
            DrawnToDressGameContext context, SubmitPlayerThemeCommand cmd)
        {
            if (context.Config.ThemeSource != ThemeSource.PlayerWritten)
            {
                context.Logger.LogWarning(
                    "SubmitPlayerTheme ignored: theme source is not PlayerWritten.");
                return null;
            }

            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "SubmitPlayerTheme: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            if (string.IsNullOrWhiteSpace(cmd.ThemeText))
            {
                context.Logger.LogWarning(
                    "SubmitPlayerTheme: empty theme text from player [{id}].", cmd.PlayerId);
                return null;
            }

            context.State.PlayerThemeSubmissions[cmd.PlayerId] = cmd.ThemeText;
            context.Logger.LogInformation(
                "Player [{id}] submitted theme: \"{text}\".", cmd.PlayerId, cmd.ThemeText);
            context.State.StateChangedEventManager.Notify();

            // Advance once every registered player has submitted.
            if (context.GamePlayers.Count > 0 &&
                context.GamePlayers.Keys.All(id => context.State.PlayerThemeSubmissions.ContainsKey(id)))
            {
                SelectThemeFromPlayerSubmissions(context);
                ApplyAnnouncementTiming(context);
                context.Logger.LogInformation(
                    "All players submitted themes. Selected: [{id}] \"{name}\".",
                    context.State.CurrentTheme?.Id, context.State.CurrentTheme?.DisplayName);
                return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(new DrawingRoundState());
            }

            return null;
        }

        private static ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?> HandleVoteForTheme(
            DrawnToDressGameContext context, VoteForThemeCommand cmd)
        {
            if (context.Config.ThemeSource != ThemeSource.RandomVoting)
            {
                context.Logger.LogWarning(
                    "VoteForTheme ignored: theme source is not RandomVoting.");
                return null;
            }

            var player = context.GetPlayer(cmd.PlayerId);
            if (player is null)
            {
                context.Logger.LogWarning(
                    "VoteForTheme: unknown player [{id}].", cmd.PlayerId);
                return null;
            }

            bool validCandidate = context.State.ThemeCandidates
                .Any(t => t.Id == cmd.ThemeId);
            if (!validCandidate)
            {
                context.Logger.LogWarning(
                    "VoteForTheme: invalid candidate [{id}] from player [{pid}].",
                    cmd.ThemeId, cmd.PlayerId);
                return null;
            }

            context.State.ThemeVotes[cmd.PlayerId] = cmd.ThemeId;
            context.Logger.LogInformation(
                "Player [{id}] voted for theme [{themeId}].", cmd.PlayerId, cmd.ThemeId);
            context.State.StateChangedEventManager.Notify();

            // Advance once every registered player has voted.
            if (context.GamePlayers.Count > 0 &&
                context.GamePlayers.Keys.All(id => context.State.ThemeVotes.ContainsKey(id)))
            {
                SelectThemeByVote(context);
                ApplyAnnouncementTiming(context);
                context.Logger.LogInformation(
                    "All players voted. Winning theme: [{id}] \"{name}\".",
                    context.State.CurrentTheme?.Id, context.State.CurrentTheme?.DisplayName);
                return ValueResult<IGameState<DrawnToDressGameContext, DrawnToDressCommand>?>.FromValue(new DrawingRoundState());
            }

            return null;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void SelectRandomTheme(DrawnToDressGameContext context)
        {
            // TODO: Replace with a proper injected theme repository in a later issue.
            var themes = FallbackThemes;
            var theme = themes[context.Random.GetRandomInt(themes.Length)];
            context.State.CurrentTheme = theme;
        }

        private static void PopulateThemeCandidates(DrawnToDressGameContext context)
        {
            var allThemes = FallbackThemes;
            int count = Math.Min(context.Config.RandomVotingCandidateCount, allThemes.Length);

            if (count < context.Config.RandomVotingCandidateCount)
            {
                context.Logger.LogWarning(
                    "RandomVotingCandidateCount ({configured}) exceeds available themes ({available}); " +
                    "capping to {count}.",
                    context.Config.RandomVotingCandidateCount, allThemes.Length, count);
            }

            // Pick a random subset without replacement using a partial Fisher-Yates shuffle.
            var indices = Enumerable.Range(0, allThemes.Length).ToArray();
            for (int i = 0; i < count; i++)
            {
                int j = context.Random.GetRandomInt(i, indices.Length);
                (indices[i], indices[j]) = (indices[j], indices[i]);
            }

            context.State.ThemeCandidates = indices.Take(count).Select(i => allThemes[i]).ToList();
        }

        private static void SelectThemeFromPlayerSubmissions(DrawnToDressGameContext context)
        {
            var submissions = context.State.PlayerThemeSubmissions.Values.ToList();
            var chosen = submissions[context.Random.GetRandomInt(submissions.Count)];
            // Trim the submitted text; use the trimmed text as both ID and display name.
            var trimmed = chosen.Trim();
            context.State.CurrentTheme = new ThemeDefinition(trimmed, trimmed);
        }

        private static void SelectThemeByVote(DrawnToDressGameContext context)
        {
            // Tally votes; ties are broken by random selection among winners.
            var tally = context.State.ThemeVotes.Values
                .GroupBy(id => id)
                .Select(g => (ThemeId: g.Key, Votes: g.Count()))
                .ToList();

            int maxVotes = tally.Max(t => t.Votes);
            var winners = tally.Where(t => t.Votes == maxVotes).ToList();
            var winnerId = winners[context.Random.GetRandomInt(winners.Count)].ThemeId;

            context.State.CurrentTheme = context.State.ThemeCandidates
                .First(t => t.Id == winnerId);
        }

        /// <summary>
        /// Sets <see cref="DrawnToDressGameState.ThemeRevealedToPlayers"/> according to the
        /// configured announcement timing.  In <see cref="ThemeAnnouncement.AfterDrawing"/>
        /// mode the flag is left <see langword="false"/> and will be set by
        /// <see cref="PoolRevealState"/> after drawing completes.
        /// </summary>
        private static void ApplyAnnouncementTiming(DrawnToDressGameContext context)
        {
            if (context.Config.ThemeAnnouncement == ThemeAnnouncement.BeforeDrawing)
            {
                context.State.ThemeRevealedToPlayers = true;
            }
            // AfterDrawing: leave ThemeRevealedToPlayers = false; PoolRevealState sets it.
        }
    }
}
