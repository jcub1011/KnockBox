using KnockBox.DndMapper.Models;
using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Helpers
{
    /// <summary>
    /// One contributor to an attribute's effective value — either the base
    /// value, or a single status effect's delta. Used to build the human-
    /// readable breakdown line shown in the roll log.
    /// </summary>
    public readonly record struct ContributionEntry(string Source, int Delta);

    /// <summary>
    /// Result of resolving a sheet attribute against the active status
    /// effects. Status effects modify the underlying attribute *value*; the
    /// attribute's scoring mode (Score → floor((v − 10) / 2), Modifier → v
    /// passthrough) then converts the effective value into the modifier used
    /// in the dice roll. This is the §8.5 fix: deltas no longer short-
    /// circuit the scoring conversion, so a −5 to a 14 INT score now gives
    /// score 9 → mod −1, not modifier 2 − 5 = −3.
    /// </summary>
    /// <param name="EffectiveValue">The base attribute value with all
    /// applicable deltas applied. Same Type as the input value; for Text
    /// attributes this is the input verbatim.</param>
    /// <param name="EffectiveModifier">The roll-time modifier derived from
    /// <paramref name="EffectiveValue"/> via its scoring mode. 0 for Text.</param>
    /// <param name="ValueBreakdown">Ordered contributors to
    /// <paramref name="EffectiveValue"/> — `[0]` is the base value, the rest
    /// are status effect deltas in encounter order.</param>
    public readonly record struct AttributeContribution(
        AttributeValue EffectiveValue,
        int EffectiveModifier,
        IReadOnlyList<ContributionEntry> ValueBreakdown);

    public static class AttributeContributionResolver
    {
        public static AttributeContribution Resolve(
            CharacterSheet sheet,
            string attributeName,
            AttributeValue baseValue)
        {
            // Collect deltas for this attribute. Text attributes don't get
            // numeric deltas applied (they have no scoring), but we still
            // record an empty breakdown so callers can render uniformly.
            var entries = new List<ContributionEntry>
            {
                new(attributeName, baseValue.IntValue ?? 0),
            };

            int deltaSum = 0;
            foreach (var effect in sheet.StatusEffects)
            {
                foreach (var d in effect.AttributeDeltas)
                {
                    if (!string.Equals(d.AttributeName, attributeName, StringComparison.Ordinal))
                        continue;
                    entries.Add(new ContributionEntry(effect.Name, d.Delta));
                    deltaSum += d.Delta;
                }
            }

            // Apply deltas to the *value*, then derive the modifier through
            // the attribute's scoring mode so e.g. a 14 INT − 5 lands at
            // mod −1 (score 9 → floor((9−10)/2) = −1) rather than the
            // arithmetic mod +2 − 5 = −3 the prior implementation produced.
            AttributeValue effective = baseValue.Type switch
            {
                AttributeValueType.Score => AttributeValue.Score((baseValue.IntValue ?? 10) + deltaSum),
                AttributeValueType.Modifier => AttributeValue.Modifier((baseValue.IntValue ?? 0) + deltaSum),
                _ => baseValue,
            };

            return new AttributeContribution(effective, effective.GetModifier() ?? 0, entries);
        }
    }
}
