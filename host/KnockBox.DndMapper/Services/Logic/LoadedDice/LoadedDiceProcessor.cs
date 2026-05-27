using System.Collections.Immutable;
using KnockBox.DndMapper.Services.State.Games.Data;
using KnockBox.DndMapper.Services.State.Games.Data.LoadedDice;

namespace KnockBox.DndMapper.Services.Logic.LoadedDice
{
    // Orchestrates rule evaluation and per-die mutation. Stateless and pure
    // aside from the RollNewDie callback on the context — kept that way so
    // tests can drive it with a deterministic RNG stub.
    public static class LoadedDiceProcessor
    {
        // Mutates `rolls` in place (record `with` replaces entries whose
        // Result changed). Returns the ordered list of rules that actually
        // fired, deduplicated by id so a rule that matches multiple dice in
        // the same roll still produces one stamp.
        public static ImmutableArray<LoadedDiceRuleStamp> Apply(
            IList<DieRoll> rolls,
            IReadOnlyList<LoadedDiceRule> rules,
            Guid? rollerSheetId,
            Func<int, LoadedDiceContext> contextForSides)
        {
            if (rolls.Count == 0 || rules.Count == 0)
                return ImmutableArray<LoadedDiceRuleStamp>.Empty;

            var matchedIds = new HashSet<Guid>();
            var stamps = ImmutableArray.CreateBuilder<LoadedDiceRuleStamp>();

            for (int i = 0; i < rolls.Count; i++)
            {
                var die = rolls[i];
                var ctx = contextForSides(die.Sides);
                int current = die.Result;

                foreach (var rule in rules)
                {
                    if (!rule.Enabled) continue;

                    // Normalize the rule's collections defensively. LoadedDiceRule
                    // is the only runtime record persisted directly to the library
                    // snapshot, so a corrupted or hand-edited save could deserialize
                    // these to null (ImmutableHashSet) or default (ImmutableArray),
                    // which would throw on .Count / iteration. Treat those as empty.
                    var targetSheetIds = rule.TargetSheetIds ?? ImmutableHashSet<Guid>.Empty;
                    var conditions = rule.Conditions.IsDefault
                        ? ImmutableArray<LoadedDiceCondition>.Empty : rule.Conditions;
                    var modifications = rule.Modifications.IsDefault
                        ? ImmutableArray<LoadedDiceModification>.Empty : rule.Modifications;

                    // Target filter: empty set means "every roll"; non-empty
                    // requires either (a) the roll's sheet id be in the set
                    // or (b) the roll have no sheet and the set contain the
                    // GmTarget sentinel ("GM" = unattributed rolls).
                    if (targetSheetIds.Count > 0)
                    {
                        bool matches =
                            (rollerSheetId is Guid sid && targetSheetIds.Contains(sid))
                            || (rollerSheetId is null && targetSheetIds.Contains(LoadedDiceRule.GmTarget));
                        if (!matches) continue;
                    }

                    bool conditionsPass = true;
                    foreach (var condition in conditions)
                    {
                        if (!condition.Matches(ctx)) { conditionsPass = false; break; }
                    }
                    if (!conditionsPass) continue;

                    foreach (var modification in modifications)
                    {
                        int next = modification.Apply(current, ctx);
                        // Clamp into the die's legal face range after every
                        // modification so a misconfigured "Set to 99" can't
                        // produce an impossible visible face.
                        if (next < 1) next = 1;
                        else if (next > die.Sides) next = die.Sides;
                        current = next;
                    }

                    if (matchedIds.Add(rule.Id))
                        stamps.Add(new LoadedDiceRuleStamp(rule.Id, rule.Name));
                }

                if (current != die.Result)
                    rolls[i] = die with { Result = current };
            }

            return stamps.ToImmutable();
        }
    }
}
