using KnockBox.Core.Services.State.Games.Shared;
using KnockBox.Services.Logic.RandomGeneration;
using KnockBox.Services.State.Games.DrawnToDress;
using KnockBox.Services.State.Games.DrawnToDress.Data;
using System.Collections.Concurrent;

namespace KnockBox.Services.Logic.Games.DrawnToDress.FSM
{
    /// <summary>
    /// Per-game context that holds shared data and helpers used by FSM states.
    /// Created once during <c>StartAsync</c> and stored on <see cref="DrawnToDressGameState"/>.
    /// </summary>
    public class DrawnToDressGameContext(DrawnToDressGameState state, ILogger logger, IRandomNumberService random)
    {
        // ── Core references ───────────────────────────────────────────────────

        /// <summary>The shared mutable game state exposed to all FSM states.</summary>
        public DrawnToDressGameState State { get; } = state;

        /// <summary>Logger shared by all FSM states.</summary>
        public ILogger Logger { get; } = logger;

        /// <summary>Random number service shared by all FSM states.</summary>
        public IRandomNumberService Random { get; } = random;

        /// <summary>
        /// The finite state machine that owns the current game flow.
        /// Assigned immediately after construction in <c>StartAsync</c>.
        /// </summary>
        public IFiniteStateMachine<DrawnToDressGameContext, DrawnToDressCommand> Fsm { get; set; } = default!;

        // ── Round tracking ──────────────────────────────────────────────────

        /// <summary>
        /// The currently active outfit round (1-based). Updated by FSM states on entry.
        /// </summary>
        public int CurrentOutfitRound { get; set; }

        // ── Convenience accessors ─────────────────────────────────────────────

        /// <summary>Shortcut to <see cref="DrawnToDressGameState.Config"/>.</summary>
        public DrawnToDressConfig Config => State.Config;

        /// <summary>Shortcut to <see cref="DrawnToDressGameState.GamePlayers"/>.</summary>
        public ConcurrentDictionary<string, DrawnToDressPlayerState> GamePlayers => State.GamePlayers;

        /// <summary>Shortcut to <see cref="DrawnToDressGameState.ClothingPool"/>.</summary>
        public ConcurrentDictionary<Guid, DrawnClothingItem> ClothingPool => State.ClothingPool;

        // ── Helper methods ────────────────────────────────────────────────────

        /// <summary>
        /// Returns the player state for <paramref name="playerId"/>, or
        /// <see langword="null"/> if the player is not in the game.
        /// </summary>
        public DrawnToDressPlayerState? GetPlayer(string playerId)
            => GamePlayers.TryGetValue(playerId, out var ps) ? ps : null;

        /// <summary>
        /// Returns <see langword="true"/> when every player has set
        /// <see cref="DrawnToDressPlayerState.IsReady"/> to <see langword="true"/>.
        /// </summary>
        public bool AllPlayersReady()
            => GamePlayers.Count > 0 && GamePlayers.Values.All(p => p.IsReady);

        /// <summary>
        /// Resets every player's <see cref="DrawnToDressPlayerState.IsReady"/> flag to
        /// <see langword="false"/> so it can be used again in the next phase.
        /// </summary>
        public void ResetReadyFlags()
        {
            foreach (var ps in GamePlayers.Values)
                ps.IsReady = false;
        }

        /// <summary>
        /// Returns <see langword="true"/> when every player has submitted an outfit
        /// (i.e. <see cref="DrawnToDressPlayerState.SubmittedOutfit"/> is not null).
        /// </summary>
        public bool AllOutfitsSubmitted()
            => GamePlayers.Count > 0 && GamePlayers.Values.All(p => p.SubmittedOutfit is not null);

        /// <summary>
        /// Returns <see langword="true"/> when every player has submitted an outfit
        /// for the specified round.
        /// </summary>
        public bool AllOutfitsSubmittedForRound(int outfitRound)
            => GamePlayers.Count > 0 && GamePlayers.Values.All(p => p.GetOutfit(outfitRound) is not null);

        /// <summary>
        /// Returns the ordered list of entrant IDs for the tournament.
        /// Each player's submitted outfits become separate entrants.
        /// </summary>
        public IReadOnlyList<EntrantId> GetTournamentEntrantIds()
        {
            var entrants = new List<EntrantId>();
            foreach (var p in GamePlayers.Values.OrderBy(p => p.PlayerId, StringComparer.Ordinal))
                foreach (var (round, _) in p.SubmittedOutfits.OrderBy(kv => kv.Key))
                    entrants.Add(new EntrantId(p.PlayerId, round));
            return entrants;
        }

        /// <summary>
        /// Looks up the outfit submission for a given entrant ID.
        /// </summary>
        public OutfitSubmission? GetOutfitByEntrantId(EntrantId entrantId)
        {
            return GetPlayer(entrantId.PlayerId)?.GetOutfit(entrantId.Round);
        }

        /// <summary>
        /// Resets the communal pool for the given outfit round: removes picks from all
        /// previous rounds, clears all claims, and rebuilds every player's owned-item set.
        /// </summary>
        public void ResetPoolForRound(int outfitRound)
        {
            // Collect all item IDs that were selected in any previous outfit round.
            var previousPicks = GamePlayers.Values
                .SelectMany(p => p.SubmittedOutfits
                    .Where(kv => kv.Key < outfitRound)
                    .SelectMany(kv => kv.Value.SelectedItemsByType.Values))
                .ToHashSet();

            // Update pool membership and clear all claims.
            foreach (var item in ClothingPool.Values)
            {
                if (previousPicks.Contains(item.Id))
                    item.IsInPool = false;
                item.ClaimedByPlayerId = null;
            }

            // Rebuild each player's owned-item set.
            foreach (var player in GamePlayers.Values)
            {
                player.OwnedClothingItemIds.Clear();

                // Self-drawn items that are still in the pool are automatically owned.
                foreach (var item in ClothingPool.Values)
                {
                    if (item.IsInPool &&
                        string.Equals(item.CreatorPlayerId, player.PlayerId, StringComparison.Ordinal))
                    {
                        player.OwnedClothingItemIds.Add(item.Id);
                    }
                }

                // When reuse is permitted, add the player's own previous outfit picks back.
                if (Config.CanReuseOutfit1Items)
                {
                    foreach (var (round, outfit) in player.SubmittedOutfits)
                    {
                        if (round >= outfitRound) continue;
                        foreach (var itemId in outfit.SelectedItemsByType.Values)
                        {
                            if (!player.OwnedClothingItemIds.Contains(itemId))
                                player.OwnedClothingItemIds.Add(itemId);
                        }
                    }
                }
            }
        }
    }
}
