using System.Collections.Generic;
using System.Linq;
using System.Text;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Helpers
{
    /// <summary>
    /// Translates a <see cref="RollResult"/> into the dice-box-threejs forced-result
    /// notation (e.g. "2d6@4,3+1d8@6") so each client can run an independent physics
    /// simulation that still lands on the authoritative server-side result.
    /// Modifiers are pure math and intentionally absent — only the visual dice
    /// belong here.
    /// </summary>
    public static class DiceNotationBuilder
    {
        public static string Build(RollResult roll)
        {
            if (roll.Rolls is null || roll.Rolls.Count == 0) return string.Empty;

            // Group by sides, preserving first-appearance order so the visual
            // grouping mirrors the roll order (1d20+1d6 != 1d6+1d20).
            // d100 is special-cased: dice-box-threejs renders a single d100 as
            // a tens-only die (faces 00, 10, … 90 interpreted as 100), so a
            // raw result like 47 can't appear on one die. To show all 1–100
            // values we expand each d100 into a percentile pair: a tens die
            // (d100, face = floor(N/10)*10) and a units die (d10, face = N%10).
            // For N=100, both faces are 0 (visually "00" + "0", read as 100).
            var order = new List<int>();
            var groups = new Dictionary<int, List<int>>();
            foreach (var die in roll.Rolls)
            {
                if (die.Sides == 100)
                {
                    int tens = (die.Result / 10) * 10;
                    int units = die.Result % 10;
                    if (die.Result == 100) { tens = 0; units = 0; } // pair shows 00 + 0.
                    AddResult(order, groups, 100, tens);
                    AddResult(order, groups, 10, units);
                }
                else
                {
                    AddResult(order, groups, die.Sides, die.Result);
                }
            }

            var sb = new StringBuilder();
            for (int i = 0; i < order.Count; i++)
            {
                var sides = order[i];
                var results = groups[sides];
                if (i > 0) sb.Append('+');
                sb.Append(results.Count).Append('d').Append(sides).Append('@');
                for (int j = 0; j < results.Count; j++)
                {
                    if (j > 0) sb.Append(',');
                    sb.Append(results[j]);
                }
            }
            return sb.ToString();
        }

        private static void AddResult(List<int> order, Dictionary<int, List<int>> groups, int sides, int result)
        {
            if (!groups.TryGetValue(sides, out var list))
            {
                list = new List<int>();
                groups[sides] = list;
                order.Add(sides);
            }
            list.Add(result);
        }
    }
}
