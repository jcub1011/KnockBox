using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.ConsultTheCard.Services.Logic.Games.Data;
using KnockBox.Core.Services.Logic.RandomGeneration;
using KnockBox.ConsultTheCard.Services.State.Games;
using KnockBox.ConsultTheCard.Services.State.Games.Data;
using System.Collections.Concurrent;

namespace KnockBox.ConsultTheCard.Services.Logic.Games.FSM
{
    /// <summary>
    /// Per-game context that holds shared data and helpers used by FSM states.
    /// Created when the game starts and stored on <see cref="ConsultTheCardGameState.Context"/>.
    /// </summary>
    public class ConsultTheCardGameContext(
        ConsultTheCardGameState state,
        IRandomNumberService rng,
        ILogger logger)
    {

        // ── Core references ───────────────────────────────────────────────────

        /// <summary>The underlying game state for this game instance.</summary>
        public ConsultTheCardGameState State { get; } = state;

        /// <summary>Random number service shared by all FSM states.</summary>
        public IRandomNumberService Rng { get; } = rng;

        /// <summary>Logger shared by all FSM states.</summary>
        public ILogger Logger { get; } = logger;

        /// <summary>The FSM that manages state transitions for this game.</summary>
        public IFiniteStateMachine<ConsultTheCardGameContext, ConsultTheCardCommand> Fsm { get; set; } = null!;

        /// <summary>
        /// Indices of <see cref="WordBank"/> groups already used in this session.
        /// Prevents repeating word pairs across games in the same session.
        /// </summary>
        public HashSet<int> UsedWordPairIndices { get; } = [];

        /// <summary>
        /// Lazily loads the word bank from <c>WordPairs.csv</c> on first access so that
        /// file I/O stays off the DI construction path; failures surface at the first
        /// game start rather than at service resolution. Tests can inject a
        /// deterministic bank via <see cref="UseWordBank"/>.
        /// </summary>
        private Lazy<IReadOnlyList<WordGroup>>? _wordBank;

        private Lazy<IReadOnlyList<WordGroup>> DefaultWordBankLoader()
            => new(() => Data.WordBank.Load(Logger));

        /// <summary>
        /// The word bank providing word groups, loaded from <c>WordPairs.csv</c> on
        /// first access. Use <see cref="UseWordBank"/> to override and
        /// <see cref="ResetWordBank"/> to return to the default file-backed loader.
        /// </summary>
        public IReadOnlyList<WordGroup> WordBank
            => (_wordBank ??= DefaultWordBankLoader()).Value;

        /// <summary>
        /// Replaces the word bank with a pre-materialized list. Subsequent reads of
        /// <see cref="WordBank"/> return the provided list directly without touching
        /// the CSV loader. Intended for tests that need a deterministic bank.
        /// </summary>
        public void UseWordBank(IReadOnlyList<WordGroup> wordBank)
        {
            ArgumentNullException.ThrowIfNull(wordBank);
            _wordBank = new Lazy<IReadOnlyList<WordGroup>>(wordBank);
        }

        /// <summary>
        /// Restores the default file-backed word-bank loader. The next read of
        /// <see cref="WordBank"/> will load <c>WordPairs.csv</c> from disk.
        /// </summary>
        public void ResetWordBank()
        {
            _wordBank = DefaultWordBankLoader();
        }

        // ── Convenience accessors (delegate to State) ─────────────────────────

        /// <summary>Shortcut to <see cref="ConsultTheCardGameState.GamePlayers"/>.</summary>
        public ConcurrentDictionary<string, ConsultTheCardPlayerState> GamePlayers => State.GamePlayers;

        // ── Role assignment ───────────────────────────────────────────────────

        /// <summary>
        /// Distributes roles and secret words to all players based on player count.
        /// Role distribution: 4p=3A/1I, 5p=3A/1I/1Inf, 6p=4A/1I/1Inf, 7p=4A/2I/1Inf, 8p=5A/2I/1Inf.
        /// Players are shuffled before assignment.
        /// </summary>
        public void AssignRoles()
        {
            var playerIds = GamePlayers.Keys.ToList();
            Shuffle(playerIds);

            int count = playerIds.Count;
            var (agents, insiders, informants) = GetRoleDistribution(count);

            // Select a word pair for this game.
            var (agentWord, insiderWord) = SelectWordPair();
            State.CurrentWordPair = [agentWord, insiderWord];

            int index = 0;

            // Assign Agent roles.
            for (int i = 0; i < agents; i++)
            {
                var ps = GamePlayers[playerIds[index++]];
                ps.Role = Role.Agent;
                ps.SecretWord = agentWord;
            }

            // Assign Insider roles.
            for (int i = 0; i < insiders; i++)
            {
                var ps = GamePlayers[playerIds[index++]];
                ps.Role = Role.Insider;
                ps.SecretWord = insiderWord;
            }

            // Assign Informant roles (0 or 1).
            for (int i = 0; i < informants; i++)
            {
                var ps = GamePlayers[playerIds[index++]];
                ps.Role = Role.Informant;
                ps.SecretWord = null;
            }

            Logger.LogDebug(
                "AssignRoles: {count}p → {agents}A/{insiders}I/{informants}Inf. AgentWord=[{aw}], InsiderWord=[{iw}].",
                count, agents, insiders, informants, agentWord, insiderWord);
        }

        /// <summary>
        /// Returns (agents, insiders, informants) counts for the given player count.
        /// </summary>
        internal static (int Agents, int Insiders, int Informants) GetRoleDistribution(int playerCount) => playerCount switch
        {
            4 => (3, 1, 0),
            5 => (3, 1, 1),
            6 => (4, 1, 1),
            7 => (4, 2, 1),
            8 => (5, 2, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(playerCount),
                $"Unsupported player count: {playerCount}. Must be 4-8.")
        };

        // ── Word pair selection ───────────────────────────────────────────────

        /// <summary>
        /// Picks a random unused <see cref="WordGroup"/> from <see cref="WordBank"/>,
        /// selects 2 words from the group, and randomly assigns which is the Agent word
        /// and which is the Insider word. Tracks used indices to avoid repeats across games.
        /// </summary>
        public (string AgentWord, string InsiderWord) SelectWordPair()
        {
            var bank = WordBank;
            if (bank.Count == 0)
                throw new InvalidOperationException("Word bank is empty.");

            // Find available indices.
            var available = Enumerable.Range(0, bank.Count)
                .Where(i => !UsedWordPairIndices.Contains(i))
                .ToList();

            // If all are used, reset the tracking.
            if (available.Count == 0)
            {
                Logger.LogWarning("SelectWordPair: all word groups used; resetting used indices.");
                UsedWordPairIndices.Clear();
                available = [.. Enumerable.Range(0, bank.Count)];
            }

            // Pick a random available group.
            int pick = available[Rng.GetRandomInt(available.Count)];
            UsedWordPairIndices.Add(pick);

            var group = bank[pick];

            // Select 2 distinct words from the group. Groups with < 2 words are
            // filtered out by the CSV loader, but a test-injected bank could
            // violate this, so guard explicitly rather than risking a livelock.
            int wordCount = group.Words.Length;
            if (wordCount < 2)
                throw new InvalidOperationException(
                    $"Word group at index {pick} has fewer than 2 words ({wordCount}); cannot select a pair.");

            // Pick two distinct indices without a rejection loop: pick the first
            // index uniformly, then offset by a non-zero amount mod wordCount.
            int wordIndex1 = Rng.GetRandomInt(wordCount);
            int wordIndex2 = (wordIndex1 + 1 + Rng.GetRandomInt(wordCount - 1)) % wordCount;

            // Randomly assign which is Agent vs Insider.
            bool swap = Rng.GetRandomInt(2) == 0;
            return swap
                ? (group.Words[wordIndex2], group.Words[wordIndex1])
                : (group.Words[wordIndex1], group.Words[wordIndex2]);
        }

        // ── Player query helpers ──────────────────────────────────────────────

        /// <summary>Returns all players who have not been eliminated.</summary>
        public List<ConsultTheCardPlayerState> GetAlivePlayers()
            => [.. GamePlayers.Values.Where(p => !p.IsEliminated)];

        /// <summary>Returns the number of players who have not been eliminated.</summary>
        public int GetAlivePlayerCount()
            => GamePlayers.Values.Count(p => !p.IsEliminated);

        /// <summary>
        /// Returns the player state for <paramref name="playerId"/>, or
        /// <see langword="null"/> if the player is not in the game.
        /// </summary>
        public ConsultTheCardPlayerState? GetPlayer(string playerId)
            => GamePlayers.TryGetValue(playerId, out var ps) ? ps : null;

        // ── Vote tallying ─────────────────────────────────────────────────────

        /// <summary>
        /// Tallies votes from all alive players who have voted.
        /// Returns the player ID with the most votes, or <see langword="null"/> on a tie.
        /// </summary>
        public string? TallyVotes()
        {
            var votes = GetAlivePlayers()
                .Where(p => p.HasVoted && p.VoteTargetId is not null)
                .GroupBy(p => p.VoteTargetId!)
                .Select(g => (TargetId: g.Key, Count: g.Count()))
                .OrderByDescending(g => g.Count)
                .ToList();

            if (votes.Count == 0)
                return null;

            // Tie check: if the top two have the same count, it's a tie.
            if (votes.Count > 1 && votes[0].Count == votes[1].Count)
                return null;

            return votes[0].TargetId;
        }

        // ── Win condition evaluation ──────────────────────────────────────────

        /// <summary>
        /// Evaluates whether the game should end and which team has won.
        /// Auto-ends when ≤2 players remain or a majority voted to end.
        /// Does NOT auto-end when all Insiders/Informant are eliminated.
        /// Priority: (1) Informant alive → Informant wins, (2) Insider alive → Insiders win, (3) Agents win.
        /// </summary>
        public WinConditionResult CheckWinConditions()
        {
            var alive = GetAlivePlayers();
            int aliveCount = alive.Count;

            // Check auto-end: ≤2 players remain.
            bool tooFewPlayers = aliveCount <= 2;

            // Check auto-end: majority voted to end.
            var endVote = State.EndGameVoteStatus;
            bool majorityVotedToEnd = endVote.VotedToEnd.Count > 0
                && endVote.RequiredVotes > 0
                && endVote.VotedToEnd.Count >= endVote.RequiredVotes;

            if (!tooFewPlayers && !majorityVotedToEnd)
                return new WinConditionResult(false, null, "Game continues.");

            string reason = tooFewPlayers
                ? $"Only {aliveCount} player(s) remain."
                : "Majority voted to end the game.";

            // Evaluate win priority: Informant > Insider > Agent.
            if (alive.Any(p => p.Role == Role.Informant))
                return new WinConditionResult(true, Role.Informant, reason);

            if (alive.Any(p => p.Role == Role.Insider))
                return new WinConditionResult(true, Role.Insider, reason);

            return new WinConditionResult(true, Role.Agent, reason);
        }

        // ── Cycle reset ───────────────────────────────────────────────────────

        /// <summary>
        /// Clears per-cycle clue/vote/end-game-vote data for all alive players.
        /// Call at the start of each new elimination cycle.
        /// </summary>
        public void ResetEliminationCycleState()
        {
            foreach (var ps in GetAlivePlayers())
            {
                ps.HasSubmittedClue = false;
                ps.CurrentClue = null;
                ps.PendingClue = null;
                ps.VoteTargetId = null;
                ps.HasVoted = false;
                ps.HasVotedToEndGame = false;
                ps.HasVotedToSkipTime = false;
            }

            State.CurrentRoundClues.Clear();
            State.CurrentRoundVotes.Clear();
            State.LastElimination = null;
            State.LastInformantGuess = null;
            State.AwaitingInformantGuess = false;

            // Reset skip time status for the next cycle.
            int aliveCount = GetAlivePlayerCount();
            int required = (aliveCount / 2) + 1;
            State.SkipTimeVoteStatus = new EndGameVoteStatus([], required);
        }

        // ── Scoring ───────────────────────────────────────────────────────────

        /// <summary>
        /// Per-cycle scoring:
        /// Agents: −1 for voting for an Agent.
        /// Insiders/Informants: +1 for surviving the round.
        /// Must be called <b>before</b> <see cref="ResetEliminationCycleState"/>.
        /// </summary>
        public void ApplyCycleScoring(string? eliminatedId)
        {
            foreach (var voter in GetAlivePlayers().Where(p => p.HasVoted && p.VoteTargetId is not null))
            {
                if (voter.Role == Role.Agent)
                {
                    var target = GetPlayer(voter.VoteTargetId!);
                    if (target is not null)
                    {
                        if (target.Role == Role.Agent)
                        {
                            voter.Score -= 1;
                            Logger.LogDebug(
                                "ApplyCycleScoring: Agent [{voter}] voted for Agent [{target}]; −1 point.",
                                voter.PlayerId, target.PlayerId);
                        }
                        else if (target.Role == Role.Insider || target.Role == Role.Informant)
                        {
                            voter.Score += 1;
                            Logger.LogDebug(
                                "ApplyCycleScoring: Agent [{voter}] correctly voted for [{role}] [{target}]; +1 point.",
                                voter.PlayerId, target.Role, target.PlayerId);
                        }
                    }
                }
            }

            foreach (var player in GetAlivePlayers())
            {
                if (player.Role == Role.Insider || player.Role == Role.Informant)
                {
                    if (player.PlayerId == eliminatedId)
                    {
                        player.Score -= 1;
                        Logger.LogDebug(
                            "ApplyCycleScoring: [{role}] [{id}] was voted out; −1 point.",
                            player.Role, player.PlayerId);
                    }
                    else
                    {
                        player.Score += 1;
                        Logger.LogDebug(
                            "ApplyCycleScoring: [{role}] [{id}] survived the round; +1 point.",
                            player.Role, player.PlayerId);
                    }
                }
            }
        }

        /// <summary>
        /// End-of-game scoring: +2 for surviving, +1 for being on the winning team,
        /// +3 for Informant correctly guessing the Agent word.
        /// Called once in GameOverState.OnEnter.
        /// </summary>
        public void ApplyEndOfGameScoring(WinConditionResult winResult)
        {
            foreach (var ps in GamePlayers.Values)
            {
                // +2 for surviving.
                if (!ps.IsEliminated)
                {
                    ps.Score += 2;
                    Logger.LogDebug(
                        "ApplyEndOfGameScoring: [{id}] survived; +2 points.", ps.PlayerId);
                }

                // +1 for being on the winning team.
                if (winResult.WinningTeam is not null && ps.Role == winResult.WinningTeam)
                {
                    ps.Score += 1;
                    Logger.LogDebug(
                        "ApplyEndOfGameScoring: [{id}] on winning team [{team}]; +1 point.",
                        ps.PlayerId, winResult.WinningTeam);
                }
            }

            // +3 for Informant correctly guessing the word.
            if (State.LastInformantGuess is { WasCorrect: true })
            {
                var informant = GetPlayer(State.LastInformantGuess.PlayerId);
                if (informant is not null)
                {
                    informant.Score += 3;
                    Logger.LogDebug(
                        "ApplyEndOfGameScoring: Informant [{id}] correctly guessed; +3 points.",
                        informant.PlayerId);
                }
            }

            // Persist scores to the cumulative GameScores dictionary.
            foreach (var ps in GamePlayers.Values)
            {
                if (!State.GameScores.TryGetValue(ps.PlayerId, out int existing))
                    existing = 0;
                State.GameScores[ps.PlayerId] = existing + ps.Score;
            }
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>Fisher-Yates shuffle using the injected RNG.</summary>
        private void Shuffle<T>(List<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = Rng.GetRandomInt(0, n + 1);
                (list[n], list[k]) = (list[k], list[n]);
            }
        }
    }
}
