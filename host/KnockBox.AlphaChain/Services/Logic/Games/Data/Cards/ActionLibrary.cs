using System.Collections.Immutable;

namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards
{
    /// <summary>
    /// The canonical, immutable catalogue of every action card. Ships with the three GDD
    /// actions; leave room for more (Sniper, etc.) by appending here.
    /// </summary>
    public static class ActionLibrary
    {
        /// <summary>Every action card, in catalogue order.</summary>
        public static readonly ImmutableArray<ActionCard> All =
        [
            new ActionCard(
                "pivot", "The Pivot",
                "Clears the required start letter for your next submission.",
                ActionKind.Pivot) { Icon = "pivot" },

            new ActionCard(
                "amnesty", "Amnesty",
                "Suppresses the Zero-Point Tax for your next submission.",
                ActionKind.Amnesty) { Icon = "amnesty" },

            new ActionCard(
                "time-thief", "Time Thief",
                "Steals 5 seconds from an opponent's shot clock.",
                ActionKind.TimeThief) { Icon = "time-thief" },
        ];

        /// <summary>Fast id → card lookup for resolving network ids against the catalogue.</summary>
        private static readonly ImmutableDictionary<string, ActionCard> ById =
            All.ToImmutableDictionary(c => c.Id, StringComparer.Ordinal);

        /// <summary>Resolves a card by its stable id, or null when the id is unknown.</summary>
        public static ActionCard? FindById(string id) =>
            ById.TryGetValue(id, out var card) ? card : null;
    }
}
