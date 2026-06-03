using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Services.State.Games.Data;

namespace KnockBox.AlphaChain.Services.Logic.Games.FSM
{
    /// <summary>
    /// Read-only standings helpers shared by the FSM. The automated-attack resolution (time-shaves,
    /// point-drains, letter-hijacks, Titanium Mirror block-and-reflect) now lives on the cards
    /// themselves (their lifecycle hooks) and the <c>IEngineEffects</c> service they resolve from the
    /// evaluation context — see <c>AlphaChainEvaluationServices</c>.
    /// </summary>
    public static class EngineEffectResolver
    {
        /// <summary>
        /// Ranks active players (rank 1 = highest score), ties broken by earliest turn-order index
        /// — the same deterministic ordering as <c>ResolveSniperBanPicker</c>/<c>GameOverState</c>.
        /// Eliminated/left players are excluded. The lowest rank equals the active player count.
        /// </summary>
        public static Dictionary<Guid, int> RankByScore(AlphaChainGameState state)
        {
            var active = state.GamePlayers.Values
                .Where(p => !p.IsEliminated && !p.HasLeft)
                .OrderByDescending(p => p.Score)
                .ThenBy(p => TurnIndex(state, p.UserId))
                .ToList();

            var ranks = new Dictionary<Guid, int>();
            for (int i = 0; i < active.Count; i++)
                ranks[active[i].UserId] = i + 1;
            return ranks;
        }

        /// <summary>
        /// The active player currently in first place (highest score; ties broken by earliest turn
        /// order), or null when no player is active. Snapshotted into
        /// <see cref="AlphaChainGameState.RoundLeaderUserId"/> at each round start for the Bounty Hunter.
        /// </summary>
        public static Guid? LeaderUserId(AlphaChainGameState state)
        {
            AlphaChainPlayerState? leader = null;
            foreach (var p in state.GamePlayers.Values)
            {
                if (p.IsEliminated || p.HasLeft) continue;
                if (leader is null
                    || p.Score > leader.Score
                    || (p.Score == leader.Score && TurnIndex(state, p.UserId) < TurnIndex(state, leader.UserId)))
                    leader = p;
            }
            return leader?.UserId;
        }

        private static int TurnIndex(AlphaChainGameState state, Guid userId)
        {
            int idx = state.TurnManager.TurnOrder.IndexOf(userId);
            return idx < 0 ? int.MaxValue : idx;
        }
    }
}
