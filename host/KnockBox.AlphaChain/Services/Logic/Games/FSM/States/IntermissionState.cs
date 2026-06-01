using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Services.State.Games.Data;
using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.State.Games.Shared;

namespace KnockBox.AlphaChain.Services.Logic.Games.FSM.States
{
    using FsmState = IGameState<AlphaChainGameContext, AlphaChainCommand>;

    /// <summary>
    /// The era-boundary Intermission. Walks deterministically through four sub-phases —
    /// <b>Deal → Expansion → Optimization → Sniper Ban</b> — then hands back to
    /// <see cref="RoundState"/> for the next era. <see cref="Tick"/> drives the timed
    /// progression; <see cref="HandleCommand"/> accepts the two player inputs
    /// (<see cref="SubmitOptimizationCommand"/>, <see cref="SelectSniperBanCommand"/>).
    /// </summary>
    /// <remarks>
    /// <para>This state is the <b>only</b> writer of <see cref="AlphaChainGameState.BannedLetter"/>
    /// after <c>SetupState</c>'s initial draw — enforced by convention, documented as a milestone
    /// invariant.</para>
    /// <para>Design calls made here (documented per the milestone's "document the call" asks):</para>
    /// <list type="bullet">
    ///   <item><b>Distinct modifiers:</b> dealt modifiers are always distinct from the cards a
    ///   player already holds. The Engine Bay keys reorders by card id, so duplicate ids would
    ///   break Optimization; this caps a player's lifetime modifiers at the library size.</item>
    ///   <item><b>Expansion is a brief visible sub-phase:</b> the +1 slot is applied when leaving
    ///   Deal, then dwells for <see cref="ExpansionAnimationSeconds"/> so the UI can animate it
    ///   before Optimization opens.</item>
    ///   <item><b>Non-submitter discard:</b> a player who never commits keeps their current bay
    ///   order; if it overflows the (now larger) slot count the <b>oldest</b> cards are discarded
    ///   first (drop from the left), keeping the freshly-dealt cards on the right.</item>
    ///   <item><b>Eliminated last-place:</b> the Sniper Ban picker is the lowest-score player that
    ///   is still active, so an eliminated last-place player is skipped in favour of the next
    ///   lowest active player.</item>
    /// </list>
    /// </remarks>
    public sealed class IntermissionState : ITimedGameState<AlphaChainGameContext, AlphaChainCommand>
    {
        /// <summary>Seconds the "dealing cards" animation dwells before Expansion.</summary>
        private const int DealAnimationSeconds = 3;

        /// <summary>Seconds the "+1 slot" animation dwells before Optimization opens.</summary>
        private const int ExpansionAnimationSeconds = 2;

        public ValueResult<FsmState?> OnEnter(AlphaChainGameContext context)
        {
            var state = context.State;
            var now = DateTimeOffset.UtcNow;

            state.SetPhase(AlphaChainGamePhase.Intermission);
            state.IntermissionPhase = IntermissionSubPhase.Deal;
            state.SubPhaseEndTime = now.AddSeconds(DealAnimationSeconds);

            DealCards(context);

            context.Logger.LogDebug(
                "Alpha Chain FSM → IntermissionState (era {era} → {next}, dealing cards)",
                state.CurrentEra, state.CurrentEra + 1);

            return null;
        }

        public Result OnExit(AlphaChainGameContext context) => Result.Success;

        public ValueResult<TimeSpan> GetRemainingTime(AlphaChainGameContext context, DateTimeOffset now)
        {
            var remaining = context.State.SubPhaseEndTime - now;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        // ── Timed progression ────────────────────────────────────────────────

        public ValueResult<FsmState?> Tick(AlphaChainGameContext context, DateTimeOffset now)
        {
            var state = context.State;

            switch (state.IntermissionPhase)
            {
                case IntermissionSubPhase.Deal:
                    if (now >= state.SubPhaseEndTime)
                        EnterExpansion(context, now);
                    return null;

                case IntermissionSubPhase.Expansion:
                    if (now >= state.SubPhaseEndTime)
                        EnterOptimization(context, now);
                    return null;

                case IntermissionSubPhase.Optimization:
                    if (now >= state.SubPhaseEndTime || AllSubmitted(state))
                        EnterSniperBan(context, now);
                    return null;

                case IntermissionSubPhase.SniperBan:
                    if (now >= state.SubPhaseEndTime)
                    {
                        // Timeout: the legal-pool draw is always legal, so this never rejects.
                        state.BannedLetter = BanLetterPool.Draw(state.Settings.BanMode, context.Rng);
                        context.Logger.LogDebug(
                            "Alpha Chain Sniper Ban timed out — random banned letter '{letter}'.", state.BannedLetter);
                        return CompleteIntermission(context);
                    }
                    return null;

                default:
                    return null;
            }
        }

        // ── Player commands ──────────────────────────────────────────────────

        public ValueResult<FsmState?> HandleCommand(AlphaChainGameContext context, AlphaChainCommand command)
        {
            return command switch
            {
                SubmitOptimizationCommand cmd => HandleSubmitOptimization(context, cmd),
                SelectSniperBanCommand cmd => HandleSelectSniperBan(context, cmd),
                _ => (ValueResult<FsmState?>)null!
            };
        }

        private static ValueResult<FsmState?> HandleSubmitOptimization(AlphaChainGameContext context, SubmitOptimizationCommand cmd)
        {
            var state = context.State;

            if (state.IntermissionPhase != IntermissionSubPhase.Optimization)
                return new ResultError("Optimization isn't open right now.",
                    $"SubmitOptimizationCommand outside Optimization (phase {state.IntermissionPhase}) from [{cmd.ActorUserId}].");

            if (!state.GamePlayers.TryGetValue(cmd.ActorUserId, out var player))
                return new ResultError("You are not in this game.",
                    $"SubmitOptimizationCommand from unknown player [{cmd.ActorUserId}].");

            var ids = cmd.ModifierBayIds;

            // Capacity guard against the (already-expanded) slot count.
            if (ids.Count > player.ModifierSlots)
                return new ResultError("That's more cards than your Engine Bay can hold.",
                    $"Optimization of {ids.Count} exceeds {player.ModifierSlots} slots for [{cmd.ActorUserId}].");

            // No duplicates — the bay is keyed by id.
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count)
                return new ResultError("A card appears more than once in that ordering.",
                    $"Duplicate card id in optimization for [{cmd.ActorUserId}].");

            // Every id must be a card the player currently holds. The dealt cards were already
            // appended to the bay in the Deal sub-phase, so "current bay" is the full candidate set.
            var heldIds = player.EngineBay.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var id in ids)
                if (!heldIds.Contains(id))
                    return new ResultError("That card isn't in your Engine Bay.",
                        $"Optimization referenced unheld card id [{id}] for [{cmd.ActorUserId}].");

            // Record but do NOT apply — the live bay is rewritten only when Optimization ends,
            // so an in-progress reorder never leaks to opponents (fog-of-war).
            state.OptimizationSubmissions[cmd.ActorUserId] =
                new OptimizationSubmission(cmd.ActorUserId, ids.ToList(), Submitted: true);

            return (ValueResult<FsmState?>)null!;
        }

        private static ValueResult<FsmState?> HandleSelectSniperBan(AlphaChainGameContext context, SelectSniperBanCommand cmd)
        {
            var state = context.State;

            if (state.IntermissionPhase != IntermissionSubPhase.SniperBan)
                return new ResultError("It's not time to pick the banned letter.",
                    $"SelectSniperBanCommand outside SniperBan (phase {state.IntermissionPhase}) from [{cmd.ActorUserId}].");

            if (cmd.ActorUserId != state.SniperBanUserId)
                return new ResultError("Only the last-place player picks the banned letter.",
                    $"Non-picker [{cmd.ActorUserId}] tried to pick (picker is [{state.SniperBanUserId}]).");

            if (!BanLetterPool.IsLegal(state.Settings.BanMode, cmd.Letter))
                return new ResultError("That letter can't be banned this match.",
                    $"Illegal Sniper Ban letter '{cmd.Letter}' under {state.Settings.BanMode}.");

            state.BannedLetter = char.ToLowerInvariant(cmd.Letter);
            context.Logger.LogDebug(
                "Alpha Chain Sniper Ban: [{picker}] banned '{letter}' for the next era.",
                cmd.ActorUserId, state.BannedLetter);

            return CompleteIntermission(context);
        }

        // ── Sub-phase entry helpers ──────────────────────────────────────────

        /// <summary>Deal: weighted (currently uniform) draws appended to each active player's hand.</summary>
        private static void DealCards(AlphaChainGameContext context)
        {
            var state = context.State;
            int modCount = state.Settings.ModifiersDealtPerEra;
            int actCount = state.Settings.ActionsDealtPerEra;

            foreach (var player in ActivePlayers(state))
            {
                // Distinct modifiers the player doesn't already hold (bay ids must stay unique).
                var heldIds = player.EngineBay.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
                var pool = ModifierLibrary.All.Where(c => !heldIds.Contains(c.Id)).ToList();
                for (int i = 0; i < modCount && pool.Count > 0; i++)
                {
                    int idx = context.Rng.GetRandomInt(pool.Count);
                    player.EngineBay.Add(pool[idx]); // append-to-right; resequenced in Optimization.
                    pool.RemoveAt(idx);
                }

                // Actions may repeat in hand.
                for (int i = 0; i < actCount && ActionLibrary.All.Length > 0; i++)
                {
                    int idx = context.Rng.GetRandomInt(ActionLibrary.All.Length);
                    player.ActionHand.Add(ActionLibrary.All[idx]);
                }
            }
        }

        /// <summary>Expansion: +1 modifier slot for every active player, then a brief dwell.</summary>
        private static void EnterExpansion(AlphaChainGameContext context, DateTimeOffset now)
        {
            var state = context.State;

            foreach (var player in ActivePlayers(state))
                player.ModifierSlots += 1;

            state.IntermissionPhase = IntermissionSubPhase.Expansion;
            state.SubPhaseEndTime = now.AddSeconds(ExpansionAnimationSeconds);

            context.Logger.LogDebug("Alpha Chain Intermission → Expansion (+1 slot for all active players).");
        }

        /// <summary>Optimization: seed each active player's pending order and open the countdown.</summary>
        private static void EnterOptimization(AlphaChainGameContext context, DateTimeOffset now)
        {
            var state = context.State;

            state.OptimizationSubmissions.Clear();
            foreach (var player in ActivePlayers(state))
            {
                var ids = player.EngineBay.Select(c => c.Id).ToList();
                state.OptimizationSubmissions[player.UserId] =
                    new OptimizationSubmission(player.UserId, ids, Submitted: false);
            }

            state.IntermissionPhase = IntermissionSubPhase.Optimization;
            state.SubPhaseEndTime = now.AddSeconds(state.Settings.IntermissionCardSelectSeconds);

            context.Logger.LogDebug("Alpha Chain Intermission → Optimization ({count} players).",
                state.OptimizationSubmissions.Count);
        }

        /// <summary>Sniper Ban: apply pending orders, resolve the last-place picker, open the countdown.</summary>
        private static void EnterSniperBan(AlphaChainGameContext context, DateTimeOffset now)
        {
            var state = context.State;

            ApplyOptimizationSubmissions(state);
            state.SniperBanUserId = ResolveSniperBanPicker(state);

            state.IntermissionPhase = IntermissionSubPhase.SniperBan;
            state.SubPhaseEndTime = now.AddSeconds(state.Settings.SniperBanSeconds);

            context.Logger.LogDebug("Alpha Chain Intermission → SniperBan (picker [{picker}]).",
                state.SniperBanUserId ?? "—");
        }

        /// <summary>
        /// Rewrites every active player's live Engine Bay from their pending submission. Submitters
        /// get exactly their chosen ordering; non-submitters keep their current bay, discarding the
        /// oldest cards first if it overflows the expanded slot count.
        /// </summary>
        private static void ApplyOptimizationSubmissions(AlphaChainGameState state)
        {
            foreach (var player in ActivePlayers(state))
            {
                int slots = player.ModifierSlots;

                if (state.OptimizationSubmissions.TryGetValue(player.UserId, out var sub) && sub.Submitted)
                {
                    var byId = player.EngineBay.ToDictionary(c => c.Id, StringComparer.Ordinal);
                    var reordered = new List<ModifierCard>(sub.ModifierBayIds.Count);
                    foreach (var id in sub.ModifierBayIds)
                        if (byId.TryGetValue(id, out var card))
                            reordered.Add(card);

                    if (reordered.Count > slots)
                        reordered = reordered.Take(slots).ToList(); // defensive; command already capped.

                    player.EngineBay.Clear();
                    player.EngineBay.AddRange(reordered);
                }
                else if (player.EngineBay.Count > slots)
                {
                    // Non-submitter overflow: discard oldest-first (drop from the left).
                    player.EngineBay.RemoveRange(0, player.EngineBay.Count - slots);
                }
            }
        }

        /// <summary>Lowest-score active player; ties broken by earliest turn-order index. Null if none active.</summary>
        private static string? ResolveSniperBanPicker(AlphaChainGameState state)
            => ActivePlayers(state)
                .OrderBy(p => p.Score)
                .ThenBy(p => TurnIndex(state, p.UserId))
                .Select(p => p.UserId)
                .FirstOrDefault();

        // ── Completion ───────────────────────────────────────────────────────

        /// <summary>
        /// Finishes the Intermission: advances the era and round counters, clears the chain so the
        /// next era starts with a free choice, and returns to <see cref="RoundState"/> (whose
        /// <c>OnEnter</c> re-arms the shot clock). The <c>CurrentEra &gt; EraCount</c> branch is a
        /// defensive backstop — <see cref="RoundState"/> ends the game on the final scheduled round,
        /// so an Intermission never runs after the last era.
        /// </summary>
        private static ValueResult<FsmState?> CompleteIntermission(AlphaChainGameContext context)
        {
            var state = context.State;

            state.IntermissionPhase = IntermissionSubPhase.Complete;
            state.CurrentEra++;
            state.CurrentRound++;
            state.RequiredStartLetter = null;
            state.SniperBanUserId = null;
            state.OptimizationSubmissions.Clear();

            if (state.CurrentEra > state.Settings.EraCount)
                return new GameOverState();

            return new RoundState();
        }

        // ── Shared helpers ───────────────────────────────────────────────────

        private static IEnumerable<AlphaChainPlayerState> ActivePlayers(AlphaChainGameState state)
            => state.GamePlayers.Values.Where(p => !p.IsEliminated && !p.HasLeft);

        private static bool AllSubmitted(AlphaChainGameState state)
            => state.OptimizationSubmissions.Count > 0
               && state.OptimizationSubmissions.Values.All(s => s.Submitted);

        private static int TurnIndex(AlphaChainGameState state, string userId)
        {
            int idx = state.TurnManager.TurnOrder.IndexOf(userId);
            return idx < 0 ? int.MaxValue : idx;
        }
    }
}
