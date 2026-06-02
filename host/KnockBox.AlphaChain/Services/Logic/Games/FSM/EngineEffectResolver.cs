using KnockBox.AlphaChain.Services.Logic.Games.Data.Cards;
using KnockBox.AlphaChain.Services.State.Games;
using KnockBox.AlphaChain.Services.State.Games.Data;

namespace KnockBox.AlphaChain.Services.Logic.Games.FSM
{
    /// <summary>
    /// Resolves the automated, rule-driven engine effects that replaced the abolished hand-played
    /// reaction tier: Flak Cannon / Scattershot time-shaves, the Bounty Hunter's leader drain,
    /// Tracer Round / Bait &amp; Switch letter-hijacks, and The Titanium Mirror's block-and-reflect.
    /// Static and state-only; every method runs inside the execute lock.
    /// <para>
    /// <b>Zero manual targeting, zero opponent-UI disruption:</b> every effect fires from a
    /// leaderboard or linguistic rule, never a point-and-click. Attacks route through
    /// <see cref="FireTimeShave"/> / <see cref="FirePointDrain"/> / <see cref="FireLetterHijack"/>,
    /// each of which gives the victim's Titanium Mirror a chance to block and reflect the hit back
    /// at its caster, decaying the shield's multiplier by its per-block step. <b>Single-pass, no
    /// cascade:</b> a reflected hit always lands on the caster and is never itself re-reflected.
    /// </para>
    /// </summary>
    public static class EngineEffectResolver
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

        /// <summary>
        /// The active player currently in first place (highest score; ties broken by earliest turn
        /// order), or null when no player is active. Snapshotted into
        /// <see cref="AlphaChainGameState.RoundLeaderUserId"/> at each round start for the Bounty Hunter.
        /// </summary>
        public static string? LeaderUserId(AlphaChainGameState state)
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

        private static int TurnIndex(AlphaChainGameState state, string userId)
        {
            int idx = state.TurnManager.TurnOrder.IndexOf(userId);
            return idx < 0 ? int.MaxValue : idx;
        }

        private static IEnumerable<AlphaChainPlayerState> OrderedActivePlayers(AlphaChainGameState state)
            => state.GamePlayers.Values
                .Where(p => !p.IsEliminated && !p.HasLeft)
                .OrderBy(p => TurnIndex(state, p.UserId));

        // ── Automated aggression (resolved after an accepted word is scored) ────

        /// <summary>
        /// Fires every <see cref="ModifierCard.AutoTimeShave"/> the submitter holds at its matching
        /// opponents: Flak Cannon shaves the next clock of every player scoring higher than the
        /// submitter; Scattershot shaves every opponent who has played a double-letter word this era.
        /// Each shave routes through the victim's Titanium Mirror.
        /// </summary>
        public static void ResolveAutoTimeShaves(
            AlphaChainGameState state, AlphaChainPlayerState submitter, List<EngineEffectEvent> effects)
        {
            foreach (var card in submitter.EngineBay)
            {
                if (card.AutoTimeShave is not { } shave) continue;

                foreach (var opp in OrderedActivePlayers(state))
                {
                    if (opp.UserId == submitter.UserId) continue;

                    bool match = shave.Target switch
                    {
                        AutoTimeShaveTarget.HigherScore => opp.Score > submitter.Score,
                        AutoTimeShaveTarget.PlayedDoubleLetterThisEra => opp.PlayedDoubleLetterWordThisEra,
                        _ => false,
                    };
                    if (match)
                        FireTimeShave(card, submitter, opp, shave.Seconds, effects);
                }
            }
        }

        /// <summary>
        /// The Bounty Hunter: if the submitter is the round's marked leader and played a word shorter
        /// than a holder's <see cref="LeaderPenaltyRule.MinLength"/>, that holder docks them
        /// <see cref="LeaderPenaltyRule.Penalty"/> points (routed through the leader's Titanium Mirror —
        /// a reflected drain hits the bounty's owner instead).
        /// </summary>
        public static void ResolveBountyHunter(
            AlphaChainGameState state, AlphaChainPlayerState submitter, int wordLength, List<EngineEffectEvent> effects)
        {
            if (state.RoundLeaderUserId != submitter.UserId)
                return;

            foreach (var owner in OrderedActivePlayers(state))
            {
                if (owner.UserId == submitter.UserId) continue;
                foreach (var card in owner.EngineBay)
                    if (card.LeaderPenalty is { } lp && wordLength < lp.MinLength)
                        FirePointDrain(card, owner, submitter, lp.Penalty, effects);
            }
        }

        // ── Attacks (routed through the victim's Titanium Mirror) ───────────────

        /// <summary>Queues a shot-clock shave on <paramref name="victim"/>, or — if their Titanium
        /// Mirror deflects it — on <paramref name="caster"/> instead.</summary>
        public static void FireTimeShave(ModifierCard card, AlphaChainPlayerState caster,
            AlphaChainPlayerState victim, int seconds, List<EngineEffectEvent> effects)
        {
            if (seconds <= 0) return;

            if (TryDeflect(victim) is { } mirror)
            {
                caster.QueuedTimePenaltySeconds += seconds;
                effects.Add(ReflectEvent(mirror, victim, caster,
                    $"Reflected {card.Name} — −{seconds}s off {caster.DisplayName}'s next clock"));
                return;
            }

            victim.QueuedTimePenaltySeconds += seconds;
            effects.Add(AttackEvent(card, caster, victim, $"−{seconds}s next shot clock"));
        }

        /// <summary>Drains points from <paramref name="victim"/>, or — if their Titanium Mirror
        /// deflects it — from <paramref name="caster"/> instead.</summary>
        public static void FirePointDrain(ModifierCard card, AlphaChainPlayerState caster,
            AlphaChainPlayerState victim, int points, List<EngineEffectEvent> effects)
        {
            if (points <= 0) return;

            if (TryDeflect(victim) is { } mirror)
            {
                caster.Score = Math.Max(0, caster.Score - points);
                effects.Add(ReflectEvent(mirror, victim, caster,
                    $"Reflected {card.Name} — −{points} from {caster.DisplayName}"));
                return;
            }

            victim.Score = Math.Max(0, victim.Score - points);
            effects.Add(AttackEvent(card, caster, victim, $"−{points} points"));
        }

        /// <summary>
        /// Forces a personal banned letter onto <paramref name="victim"/> (their next word) — or, if
        /// their Titanium Mirror deflects it, onto <paramref name="caster"/> instead. A victim already
        /// carrying a personal ban is left as-is (no double-curse).
        /// </summary>
        public static void FireLetterHijack(ModifierCard card, AlphaChainPlayerState caster,
            AlphaChainPlayerState victim, char letter, List<EngineEffectEvent> effects)
        {
            letter = char.ToLowerInvariant(letter);

            if (TryDeflect(victim) is { } mirror)
            {
                caster.PersonalBannedLetter ??= letter;
                effects.Add(ReflectEvent(mirror, victim, caster,
                    $"Reflected {card.Name} — '{char.ToUpperInvariant(letter)}' banned for {caster.DisplayName}"));
                return;
            }

            if (victim.PersonalBannedLetter is not null)
                return;

            victim.PersonalBannedLetter = letter;
            effects.Add(AttackEvent(card, caster, victim, $"next word bans '{char.ToUpperInvariant(letter)}'"));
        }

        /// <summary>
        /// If <paramref name="victim"/> holds a Titanium Mirror, decays its multiplier by the shield's
        /// per-block step (floored at 0) and returns the mirror card so the caller can reflect the
        /// attack and post a notice. Returns null when the victim has no shield.
        /// </summary>
        private static ModifierCard? TryDeflect(AlphaChainPlayerState victim)
        {
            var mirror = victim.EngineBay.FirstOrDefault(c => c.Shield is not null);
            if (mirror?.Shield is not { } shield)
                return null;

            victim.ShieldMultiplier = Math.Max(0.0, victim.ShieldMultiplier - shield.DecayPerBlock);
            return mirror;
        }

        // ── Notice builders ─────────────────────────────────────────────────────

        private static EngineEffectEvent AttackEvent(ModifierCard card, AlphaChainPlayerState holder,
            AlphaChainPlayerState target, string reason) =>
            new(card.Id, card.Name, card.Icon, EngineEffectClass.Offensive,
                holder.UserId, holder.DisplayName, target.UserId, target.DisplayName, reason);

        private static EngineEffectEvent ReflectEvent(ModifierCard mirror, AlphaChainPlayerState holder,
            AlphaChainPlayerState target, string reason) =>
            new(mirror.Id, mirror.Name, mirror.Icon, EngineEffectClass.Special,
                holder.UserId, holder.DisplayName, target.UserId, target.DisplayName, reason, Negated: true);
    }
}
