using System.Collections.Immutable;

namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards
{
    /// <summary>
    /// The canonical, immutable catalogue of every reaction card, plus the tunable constants
    /// the resolver reads. Reactions sit in a player's hand and auto-fire on game events — see
    /// <see cref="ReactionTrigger"/>. Add new reactions here and they become draftable everywhere.
    /// </summary>
    public static class ReactionLibrary
    {
        // ── Stable card ids (also used by tests to deal a specific reaction) ──
        public const string AmnestyId = "amnesty";
        public const string FreeThrowId = "free-throw";
        public const string OvertimeId = "overtime";
        public const string WindfallId = "windfall";
        public const string TollBoothId = "toll-booth";
        public const string FrostbiteId = "frostbite";
        public const string JinxId = "jinx";
        public const string CensorId = "censor";
        public const string RiposteId = "riposte";

        // ── Tunable constants (kept here as the single source of truth) ──

        /// <summary>Required start letters rare enough to fire Free Throw at turn start.</summary>
        public static readonly ImmutableArray<char> RareStartLetters = ['q', 'x', 'z', 'j', 'k', 'v'];

        /// <summary>Minimum final (post-modifier) word score that counts as a "big word" for Toll Booth.</summary>
        public const int BigWordThreshold = 25;

        /// <summary>Seconds Toll Booth shaves off the targeted opponent's next shot clock.</summary>
        public const int TollBoothPenaltySeconds = 5;

        /// <summary>Seconds Frostbite shaves off the targeted opponent's next shot clock.</summary>
        public const int FrostbitePenaltySeconds = 5;

        /// <summary>Reaction cards Windfall draws when the holder drops to last place.</summary>
        public const int WindfallDrawCount = 2;

        /// <summary>
        /// Seconds Overtime adds when the holder's shot clock expires. Derived from the match's
        /// shot clock so it scales with pace (half the clock, clamped to a sane band).
        /// </summary>
        public static int OvertimeSeconds(int shotClockSeconds) => Math.Clamp(shotClockSeconds / 2, 3, 30);

        /// <summary>
        /// Deterministic priority order for Jinx's auto-picked personal banned letter — common
        /// enough to be a real threat, but routable. The first letter not already banned (by the
        /// era letter or an active Censor) is chosen, so the pick is reproducible without RNG.
        /// </summary>
        private const string JinxLetterPriority = "tsrnldcmpbgfhwy";

        /// <summary>
        /// Picks Jinx's personal banned letter: the first <see cref="JinxLetterPriority"/> letter
        /// not in <paramref name="excluded"/> (the era/censor bans), or a fallback when all collide.
        /// </summary>
        public static char PickJinxLetter(IEnumerable<char> excluded)
        {
            var taken = excluded.Where(c => c != default).Select(char.ToLowerInvariant).ToHashSet();
            foreach (char c in JinxLetterPriority)
                if (!taken.Contains(c))
                    return c;
            return JinxLetterPriority[0];
        }

        /// <summary>Every reaction card, in catalogue order.</summary>
        public static readonly ImmutableArray<ReactionCard> All =
        [
            new ReactionCard(
                AmnestyId, "Amnesty",
                "Auto: when you play a banned-letter word, the Zero-Point Tax is suppressed and the word scores in full.",
                ReactionTrigger.Amnesty)
            { Icon = "amnesty", Class = ReactionClass.Defensive },

            new ReactionCard(
                FreeThrowId, "Free Throw",
                "Auto: when your turn opens on a rare letter (Q/X/Z/J/K/V), the required start letter is cleared.",
                ReactionTrigger.FreeThrow)
            { Icon = "free-throw", Class = ReactionClass.Defensive },

            new ReactionCard(
                OvertimeId, "Overtime",
                "Auto: when your shot clock runs out, gain a few seconds and keep your turn — once.",
                ReactionTrigger.Overtime)
            { Icon = "overtime", Class = ReactionClass.Defensive },

            new ReactionCard(
                WindfallId, "Windfall",
                "Auto: when you fall to last place, immediately draw 2 more reaction cards.",
                ReactionTrigger.Windfall)
            { Icon = "windfall", Class = ReactionClass.Defensive },

            new ReactionCard(
                TollBoothId, "Toll Booth",
                "Auto: when an opponent ahead of you posts a big word, shave 5s off their next shot clock.",
                ReactionTrigger.TollBooth)
            { Icon = "toll-booth", Class = ReactionClass.Offensive, IsAttack = true },

            new ReactionCard(
                FrostbiteId, "Frostbite",
                "Auto: when an opponent overtakes you, shave time off their next shot clock.",
                ReactionTrigger.Frostbite)
            { Icon = "frostbite", Class = ReactionClass.Offensive, IsAttack = true },

            new ReactionCard(
                JinxId, "Jinx",
                "Auto: when an opponent takes the lead, curse their next word with a personal banned letter.",
                ReactionTrigger.Jinx)
            { Icon = "jinx", Class = ReactionClass.Offensive, IsAttack = true },

            new ReactionCard(
                CensorId, "Censor",
                "Auto: when you fall to last place, ban an extra letter for everyone for one round (Riposte holders are spared).",
                ReactionTrigger.Censor)
            { Icon = "censor", Class = ReactionClass.Special },

            new ReactionCard(
                RiposteId, "Riposte",
                "Auto: negate the next attack reaction aimed at you and reflect it back at its caster; also shields you from board-wide bans.",
                ReactionTrigger.Riposte)
            { Icon = "riposte", Class = ReactionClass.Special },
        ];

        /// <summary>Fast id → card lookup for resolving network ids against the catalogue.</summary>
        private static readonly ImmutableDictionary<string, ReactionCard> ById =
            All.ToImmutableDictionary(c => c.Id, StringComparer.Ordinal);

        /// <summary>Resolves a card by its stable id, or null when the id is unknown.</summary>
        public static ReactionCard? FindById(string id) =>
            ById.TryGetValue(id, out var card) ? card : null;
    }
}
