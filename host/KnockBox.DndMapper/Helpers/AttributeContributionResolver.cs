using KnockBox.DndMapper.Services.State.Games.Data;

namespace KnockBox.DndMapper.Helpers
{
    public readonly record struct ContributionEntry(string Source, int Delta);

    /// <summary>
    /// Resolves the full attribute-modifier contribution for a sheet + attribute
    /// name: the base modifier plus every StatusEffect.AttributeDelta whose
    /// AttributeName matches. Returns both the summed total and a per-source
    /// breakdown for roll-log rendering (§8.5.5).
    /// </summary>
    public static class AttributeContributionResolver
    {
        public static (int Total, IReadOnlyList<ContributionEntry> Breakdown) Resolve(
            CharacterSheet sheet,
            string attributeName,
            int baseModifier)
        {
            var entries = new List<ContributionEntry> { new(attributeName, baseModifier) };
            int total = baseModifier;
            foreach (var effect in sheet.StatusEffects)
            {
                foreach (var delta in effect.AttributeDeltas)
                {
                    if (string.Equals(delta.AttributeName, attributeName, StringComparison.Ordinal))
                    {
                        entries.Add(new ContributionEntry(effect.Name, delta.Delta));
                        total += delta.Delta;
                    }
                }
            }
            return (total, entries);
        }
    }
}
