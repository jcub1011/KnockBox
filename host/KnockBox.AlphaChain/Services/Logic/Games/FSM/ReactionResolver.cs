using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Services.State.Games.Data;

namespace KnockBox.AlphaChain.Services.Logic.Games.FSM
{
    /// <summary>
    /// Resolves auto-firing reaction cards. Static and state-only (mirrors
    /// <c>RoundState.PayTaxCollectorBounty</c>); every method runs inside the execute lock. A
    /// reaction fires only when it would help, and is consumed only when it fires.
    /// <para>
    /// <b>Single-pass, no-cascade invariant:</b> no reaction mutates a player's <i>current</i>
    /// score — they change future shot clocks (Toll Booth / Frostbite), plant future bans
    /// (Jinx / Censor), or draw cards (Windfall). The only current-score mutation in the
    /// after-score window is the existing Tax Collector bounty, and standings are ranked
    /// <i>after</i> it. So every reaction evaluates against one fixed post-state and the system
    /// cannot loop. Preserve this invariant when adding reactions.
    /// </para>
    /// </summary>
    public static class ReactionResolver
    {
        // ── Standings ─────────────────────────────────────────────────────────

        /// <summary>
        /// Ranks active players (rank 1 = highest score), ties broken by earliest turn-order index
        /// — the same deterministic ordering as <c>ResolveSniperBanPicker</c>/<c>GameOverState</c>.
        /// Eliminated/left players are excluded. The lowest rank equals the active player count.
        /// </summary>
        public static Dictionary<string, int> RankByScore(AlphaChainGameState state)
        {
            var active = state.GamePlayers.Values
                .Where(p => !p.IsEliminated && !p.HasLeft)
                .OrderByDescending(p => p.Score)
                .ThenBy(p => TurnIndex(state, p.UserId))
                .ToList();

            var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < active.Count; i++)
                ranks[active[i].UserId] = i + 1;
            return ranks;
        }

        private static int TurnIndex(AlphaChainGameState state, string userId)
        {
            int idx = state.TurnManager.TurnOrder.IndexOf(userId);
            return idx < 0 ? int.MaxValue : idx;
        }

        // ── Amnesty (resolved before the tax is finalized) ─────────────────────

        /// <summary>
        /// Fires Amnesty when the submitter holds it, the word contains a banned letter, and the
        /// word would have scored &gt; 0 (so the suppression is actually worth a card). Returns
        /// true when fired — the caller then leaves the word untaxed.
        /// </summary>
        public static bool TryAmnesty(AlphaChainPlayerState? submitter, bool containsBanned,
            int wouldBeScore, List<ReactionEvent> notices)
        {
            if (submitter is null || !containsBanned || wouldBeScore <= 0)
                return false;

            var card = submitter.ReactionHand.FirstOrDefault(c => c.Trigger == ReactionTrigger.Amnesty);
            if (card is null)
                return false;

            submitter.ReactionHand.Remove(card);
            notices.Add(Self(card, submitter, $"Zero-Point Tax suppressed (+{wouldBeScore})"));
            return true;
        }

        // ── After an accepted, scored word ──────────────────────────────────────

        /// <summary>
        /// Resolves every standings-driven reaction for an accepted word: attacks on the submitter
        /// (Jinx / Frostbite / Toll Booth, routed through the submitter's Riposte) and the self/board
        /// reactions (Windfall / Censor) for any holder who dropped to last place. <paramref name="preRanks"/>
        /// is the standings captured before this word was credited; current state is the post-state.
        /// </summary>
        public static void ResolveAfterScore(AlphaChainGameContext context, string submitterUserId,
            int finalScore, int wordLength, IReadOnlyDictionary<string, int> preRanks, List<ReactionEvent> notices)
        {
            var state = context.State;
            if (!state.GamePlayers.TryGetValue(submitterUserId, out var submitter))
                return;

            var postRanks = RankByScore(state);
            if (!postRanks.TryGetValue(submitterUserId, out int submitterPost))
                return;

            int activeCount = postRanks.Count;
            int submitterPre = preRanks.TryGetValue(submitterUserId, out var sp) ? sp : int.MaxValue;
            var holders = OrderedActiveHolders(state);

            // The submitter's single Riposte negates the FIRST attack of this event only; the
            // reflection it produces always lands and is never itself Riposte'd (loop guard).
            // "First" is by the fixed, deterministic firing order below — Jinx (1a) → Frostbite
            // (1b) → Toll Booth (1c) — so when the submitter would eat several attacks in one
            // submission, the Riposte always reflects the earliest in that order and the rest
            // land. This is intentional (blocks the first, not the "worst", attack); keep the
            // order stable so replays are reproducible.
            bool riposteAvailable = submitter.ReactionHand.Any(c => c.Trigger == ReactionTrigger.Riposte);

            // 1a. Jinx — fires when the submitter takes the overall lead.
            if (submitterPost == 1 && submitterPre != 1)
                foreach (var h in holders.Where(h => h.UserId != submitterUserId))
                    if (h.ReactionHand.FirstOrDefault(c => c.Trigger == ReactionTrigger.Jinx) is { } jinx)
                        ApplyJinx(state, jinx, h, submitter, ref riposteAvailable, notices);

            // 1b. Frostbite — fires for each opponent the submitter overtook specifically.
            foreach (var h in holders.Where(h => h.UserId != submitterUserId))
            {
                int hPre = preRanks.GetValueOrDefault(h.UserId, int.MaxValue);
                int hPost = postRanks.GetValueOrDefault(h.UserId, int.MaxValue);
                if (submitterPre >= hPre && submitterPost < hPost &&
                    h.ReactionHand.FirstOrDefault(c => c.Trigger == ReactionTrigger.Frostbite) is { } fb)
                    ApplyTimeAttack(fb, h, submitter, ReactionLibrary.FrostbitePenaltySeconds, ref riposteAvailable, notices);
            }

            // 1c. Toll Booth — a behind opponent fires when the (ahead) submitter posts a long word,
            //     stealing a cut of the points just earned. Unlike the time attacks this transfers
            //     current score (like the Tax Collector bounty); postRanks stay fixed, so no cascade.
            if (wordLength >= ReactionLibrary.TollBoothMinLength)
                foreach (var h in holders.Where(h => h.UserId != submitterUserId))
                {
                    int hPost = postRanks.GetValueOrDefault(h.UserId, int.MaxValue);
                    if (submitterPost < hPost &&
                        h.ReactionHand.FirstOrDefault(c => c.Trigger == ReactionTrigger.TollBooth) is { } tb)
                        ApplyPointSteal(tb, h, submitter, finalScore, ref riposteAvailable, notices);
                }

            // 2. Self/board reactions for any holder who just fell to last place.
            if (activeCount > 1)
                foreach (var h in holders)
                {
                    int hPre = preRanks.GetValueOrDefault(h.UserId, int.MaxValue);
                    int hPost = postRanks.GetValueOrDefault(h.UserId, int.MaxValue);
                    if (hPost != activeCount || hPre == activeCount)
                        continue;

                    // Windfall fires at most once per era so a player riding the 3rd/4th boundary
                    // can't loop-draw their whole deck.
                    if (!h.WindfallFiredThisEra &&
                        h.ReactionHand.FirstOrDefault(c => c.Trigger == ReactionTrigger.Windfall) is { } wf)
                        ApplyWindfall(context, wf, h, notices);

                    if (state.CensorBannedLetter is null &&
                        h.ReactionHand.FirstOrDefault(c => c.Trigger == ReactionTrigger.Censor) is { } cs)
                        ApplyCensor(state, cs, h, notices);
                }
        }

        // ── Turn start (Free Throw) ─────────────────────────────────────────────

        /// <summary>
        /// Fires Free Throw when the now-active player holds it and the required start letter is a
        /// rare one (Q/X/Z/J/K/V) — clearing the requirement for their turn. Returns true if fired.
        /// </summary>
        public static bool TryFreeThrow(AlphaChainGameState state, List<ReactionEvent> notices)
        {
            if (state.TurnManager.CurrentPlayer is not { } id ||
                !state.GamePlayers.TryGetValue(id, out var player) ||
                player.IsEliminated || player.HasLeft ||
                state.RequiredStartLetter is not { } req ||
                !ReactionLibrary.RareStartLetters.Contains(char.ToLowerInvariant(req)))
                return false;

            var card = player.ReactionHand.FirstOrDefault(c => c.Trigger == ReactionTrigger.FreeThrow);
            if (card is null)
                return false;

            player.ReactionHand.Remove(card);
            state.RequiredStartLetter = null;
            notices.Add(Self(card, player, $"Cleared the required '{char.ToUpperInvariant(req)}'"));
            return true;
        }

        // ── Shot-clock expiry (Overtime) ────────────────────────────────────────

        /// <summary>
        /// Fires Overtime when the current player holds it as their shot clock expires — extending
        /// the clock so the turn continues (this prevents a 0-score timeout, or elimination in
        /// Survival, so it is always beneficial). Returns true if fired; the caller then skips the
        /// timeout consequence.
        /// </summary>
        public static bool TryOvertime(AlphaChainGameState state, DateTimeOffset now, List<ReactionEvent> notices)
        {
            if (state.TurnManager.CurrentPlayer is not { } id ||
                !state.GamePlayers.TryGetValue(id, out var player) ||
                player.IsEliminated || player.HasLeft)
                return false;

            var card = player.ReactionHand.FirstOrDefault(c => c.Trigger == ReactionTrigger.Overtime);
            if (card is null)
                return false;

            player.ReactionHand.Remove(card);
            int secs = ReactionLibrary.OvertimeSeconds(state.Settings.ShotClockSeconds);
            state.PhaseEndTime = now.AddSeconds(secs);
            notices.Add(Self(card, player, $"+{secs}s — turn continues"));
            return true;
        }

        // ── Effect application ──────────────────────────────────────────────────

        private static void ApplyJinx(AlphaChainGameState state, ReactionCard card,
            AlphaChainPlayerState caster, AlphaChainPlayerState victim, ref bool riposteAvailable,
            List<ReactionEvent> notices)
        {
            if (riposteAvailable && TakeRiposte(victim) is { } rip)
            {
                riposteAvailable = false;
                caster.PersonalBannedLetter ??= PickBan(state);
                caster.ReactionHand.Remove(card);
                notices.Add(Reflect(rip, victim, caster, $"Reflected {card.Name} back at {caster.DisplayName}"));
                TryFeedbackLoop(victim, caster, notices);
                return;
            }

            // No extra benefit if the target is already cursed — keep the card.
            if (victim.PersonalBannedLetter is not null)
                return;

            char letter = PickBan(state);
            victim.PersonalBannedLetter = letter;
            caster.ReactionHand.Remove(card);
            notices.Add(Attack(card, caster, victim, $"next word bans '{char.ToUpperInvariant(letter)}'"));
        }

        private static void ApplyTimeAttack(ReactionCard card, AlphaChainPlayerState caster,
            AlphaChainPlayerState victim, int seconds, ref bool riposteAvailable, List<ReactionEvent> notices)
        {
            if (riposteAvailable && TakeRiposte(victim) is { } rip)
            {
                riposteAvailable = false;
                caster.QueuedTimePenaltySeconds += seconds;
                caster.ReactionHand.Remove(card);
                notices.Add(Reflect(rip, victim, caster, $"Reflected {card.Name} — −{seconds}s {caster.DisplayName}'s next clock"));
                TryFeedbackLoop(victim, caster, notices);
                return;
            }

            victim.QueuedTimePenaltySeconds += seconds;
            caster.ReactionHand.Remove(card);
            notices.Add(Attack(card, caster, victim, $"−{seconds}s next shot clock"));
        }

        /// <summary>
        /// Toll Booth's point-steal: the (behind) caster takes <see cref="ReactionLibrary.TollBoothStealFraction"/>
        /// of the points the (ahead) submitter just earned. Routed through the victim's Riposte —
        /// when reflected, the caster loses the points to the victim instead. Transfers current
        /// score; the caller's <c>postRanks</c> snapshot is intentionally not recomputed (no cascade).
        /// </summary>
        private static void ApplyPointSteal(ReactionCard card, AlphaChainPlayerState caster,
            AlphaChainPlayerState victim, int finalScore, ref bool riposteAvailable, List<ReactionEvent> notices)
        {
            int amount = (int)Math.Round(finalScore * ReactionLibrary.TollBoothStealFraction, MidpointRounding.AwayFromZero);
            if (amount <= 0)
                return; // nothing worth stealing (e.g. a taxed long word) — keep the card.

            if (riposteAvailable && TakeRiposte(victim) is { } rip)
            {
                riposteAvailable = false;
                caster.Score = Math.Max(0, caster.Score - amount);
                victim.Score += amount;
                caster.ReactionHand.Remove(card);
                notices.Add(Reflect(rip, victim, caster, $"Reflected {card.Name} — took {amount} back from {caster.DisplayName}"));
                TryFeedbackLoop(victim, caster, notices);
                return;
            }

            victim.Score = Math.Max(0, victim.Score - amount);
            caster.Score += amount;
            caster.ReactionHand.Remove(card);
            notices.Add(Attack(card, caster, victim, $"stole {amount} points"));
        }

        /// <summary>
        /// Fires a Feedback Loop the <paramref name="riposter"/> holds (consuming it) when their
        /// Riposte negated <paramref name="attacker"/>: queues a turn-start silence on the attacker.
        /// </summary>
        private static void TryFeedbackLoop(AlphaChainPlayerState riposter, AlphaChainPlayerState attacker,
            List<ReactionEvent> notices)
        {
            var fb = riposter.ReactionHand.FirstOrDefault(c => c.Trigger == ReactionTrigger.FeedbackLoop);
            if (fb is null)
                return;

            riposter.ReactionHand.Remove(fb);
            attacker.QueuedSilenceSeconds = Math.Max(attacker.QueuedSilenceSeconds, ReactionLibrary.FeedbackLoopSilenceSeconds);
            notices.Add(Attack(fb, riposter, attacker,
                $"silenced {attacker.DisplayName} for {ReactionLibrary.FeedbackLoopSilenceSeconds}s"));
        }

        private static void ApplyWindfall(AlphaChainGameContext context, ReactionCard card,
            AlphaChainPlayerState holder, List<ReactionEvent> notices)
        {
            holder.ReactionHand.Remove(card);
            holder.WindfallFiredThisEra = true;

            int drawn = 0;
            for (int i = 0; i < ReactionLibrary.WindfallDrawCount && ReactionLibrary.All.Length > 0; i++)
            {
                int idx = context.Rng.GetRandomInt(ReactionLibrary.All.Length);
                holder.ReactionHand.Add(ReactionLibrary.All[idx]);
                drawn++;
            }

            notices.Add(Self(card, holder, $"Drew {drawn} reaction{(drawn == 1 ? "" : "s")}"));
        }

        // Censor's exemption set is a SNAPSHOT taken at impose time: every player who holds a
        // Riposte at this instant is spared for the round. This is deliberately not retroactive —
        // a player who acquires a Riposte later in the same round (e.g. via a Windfall draw) is
        // NOT added to the exempt set and is bound by the ban. Keep the card text in
        // ReactionLibrary aligned with this "spared if holding Riposte when Censor lands" rule.
        private static void ApplyCensor(AlphaChainGameState state, ReactionCard card,
            AlphaChainPlayerState holder, List<ReactionEvent> notices)
        {
            holder.ReactionHand.Remove(card);

            char letter = PickBan(state);
            state.CensorBannedLetter = letter;
            state.CensorImposedAtRound = state.CurrentRound;
            state.CensorExemptUserIds.Clear();
            foreach (var p in state.GamePlayers.Values)
                if (!p.IsEliminated && !p.HasLeft && p.ReactionHand.Any(c => c.Trigger == ReactionTrigger.Riposte))
                    state.CensorExemptUserIds.Add(p.UserId);

            notices.Add(new ReactionEvent(
                card.Id, card.Name, card.Icon, card.Class, holder.UserId, holder.DisplayName,
                null, null, $"Banned '{char.ToUpperInvariant(letter)}' for everyone this round"));
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>Picks a deterministic ban letter that avoids the current era/censor letters.</summary>
        private static char PickBan(AlphaChainGameState state) =>
            ReactionLibrary.PickJinxLetter([state.BannedLetter ?? default, state.CensorBannedLetter ?? default]);

        private static ReactionCard? TakeRiposte(AlphaChainPlayerState victim)
        {
            var card = victim.ReactionHand.FirstOrDefault(c => c.Trigger == ReactionTrigger.Riposte);
            if (card is not null)
                victim.ReactionHand.Remove(card);
            return card;
        }

        private static List<AlphaChainPlayerState> OrderedActiveHolders(AlphaChainGameState state) =>
            state.GamePlayers.Values
                .Where(p => !p.IsEliminated && !p.HasLeft)
                .OrderBy(p => TurnIndex(state, p.UserId))
                .ToList();

        private static ReactionEvent Self(ReactionCard card, AlphaChainPlayerState holder, string reason) =>
            new(card.Id, card.Name, card.Icon, card.Class, holder.UserId, holder.DisplayName, null, null, reason);

        private static ReactionEvent Attack(ReactionCard card, AlphaChainPlayerState holder,
            AlphaChainPlayerState target, string reason) =>
            new(card.Id, card.Name, card.Icon, card.Class, holder.UserId, holder.DisplayName,
                target.UserId, target.DisplayName, reason);

        private static ReactionEvent Reflect(ReactionCard riposte, AlphaChainPlayerState holder,
            AlphaChainPlayerState target, string reason) =>
            new(riposte.Id, riposte.Name, riposte.Icon, riposte.Class, holder.UserId, holder.DisplayName,
                target.UserId, target.DisplayName, reason, Negated: true);
    }
}
