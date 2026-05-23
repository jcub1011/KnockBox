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

            // dice-box-threejs has no 100-face geometry — d100 renders as a
            // tens-only die. Expand each d100 result into a percentile pair
            // (tens die + d10 units die) so all 1–100 values are visible. For
            // N=100, both faces are 0 (visually "00" + "0", read as 100).
            //
            // Critical: the library's parseNotation only honors ONE "@" suffix
            // — everything after the first "@" is regex-scanned for *all*
            // forced result values across *all* dice groups, in dice-creation
            // order. So per-group "@..." breaks (the second group never gets
            // added to the dice set). We emit all dice groups first, joined
            // by "+", then a single "@" followed by results in dice order.
            var diceOrder = new List<int>();              // sides per spawned die, in order
            var sidesGroups = new List<(int sides, int count)>();
            var results = new List<int>();
            int? lastSides = null;
            int runCount = 0;

            void FlushRun()
            {
                if (lastSides is int s && runCount > 0)
                {
                    sidesGroups.Add((s, runCount));
                }
                runCount = 0;
                lastSides = null;
            }

            void AddDie(int sides, int result)
            {
                diceOrder.Add(sides);
                results.Add(result);
                if (lastSides == sides) runCount++;
                else { FlushRun(); lastSides = sides; runCount = 1; }
            }

            foreach (var die in roll.Rolls)
            {
                if (die.Sides == 100)
                {
                    int tens = (die.Result == 100) ? 0 : (die.Result / 10) * 10;
                    int units = (die.Result == 100) ? 0 : (die.Result % 10);
                    AddDie(100, tens);
                    AddDie(10, units);
                }
                else
                {
                    AddDie(die.Sides, die.Result);
                }
            }
            FlushRun();

            var sb = new StringBuilder();
            for (int i = 0; i < sidesGroups.Count; i++)
            {
                if (i > 0) sb.Append('+');
                sb.Append(sidesGroups[i].count).Append('d').Append(sidesGroups[i].sides);
            }
            sb.Append('@');
            for (int j = 0; j < results.Count; j++)
            {
                if (j > 0) sb.Append(',');
                sb.Append(results[j]);
            }
            return sb.ToString();
        }
    }
}
