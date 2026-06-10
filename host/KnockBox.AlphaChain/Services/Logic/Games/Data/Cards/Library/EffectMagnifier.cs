namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library
{
    /// <summary>
    /// How a submitted magnification picks the card(s) it applies to, relative to the magnifier's own
    /// slot in the bay. The service owns this mapping so cards never reference a neighbor by index.
    /// </summary>
    public enum MagnificationApplicationRule
    {
        /// <summary>The single card directly to the magnifier's right (the Magnifying Glass rule).</summary>
        ImmediateRightNeighbor,
    }

    /// <summary>
    /// A per-evaluation registry that magnifier cards (the Magnifying Glass) <i>push</i> their
    /// magnification into and every other card <i>pulls</i> the magnification that applies to itself
    /// from. It is deliberately dumb: it only maps a submitter's position + <see cref="MagnificationApplicationRule"/>
    /// to the target card(s) and accumulates the product per target. It knows nothing about
    /// "stacking" — compounding emerges from the ordered populate walk plus each magnifier folding the
    /// magnification already applied to <i>itself</i> into what it submits (see
    /// <see cref="EffectMagnifier"/>). Cards decide how their own numbers are scaled by the result.
    /// </summary>
    public interface IEffectMagnifier
    {
        /// <summary>Records a magnification emitted by the card currently being populated, applied to the
        /// card(s) the <paramref name="rule"/> selects relative to that submitter's slot.</summary>
        void SubmitMagnification(double magnification, MagnificationApplicationRule rule);

        /// <summary>The accumulated magnification that applies to <paramref name="card"/> (1.0 when none).</summary>
        double GetMagnification(IModifierCard card);
    }

    /// <summary>
    /// The concrete <see cref="IEffectMagnifier"/>. Built once from a player's ordered bay; the
    /// constructor walks the bay strictly left → right and lets each card push its magnifications. That
    /// ordering is the whole trick: when a magnifier is populated it can read the magnification already
    /// applied to itself (a glass to its left) and fold it into the value it submits for the next card,
    /// so two glasses in series compound (1.5 × 1.5 = 2.25, three → 3.375) without this service or any
    /// card knowing anything about its neighbors.
    /// </summary>
    public sealed class EffectMagnifier : IEffectMagnifier
    {
        private readonly IReadOnlyList<IModifierCard> _bay;
        private readonly Dictionary<IModifierCard, int> _indexOf;
        private readonly Dictionary<int, double> _byTarget = new();
        private int _current;

        public EffectMagnifier(IReadOnlyList<IModifierCard> bay)
        {
            _bay = bay;
            // Keyed by reference identity: each bay holds distinct factory-created card instances, so a
            // card maps to its own slot. The same instance appearing twice in one bay would collapse to
            // its last index — an invariant the engine upholds by never sharing a card instance across slots.
            _indexOf = new Dictionary<IModifierCard, int>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < bay.Count; i++)
                _indexOf[bay[i]] = i;

            // Populate left → right so a magnifier sees the magnification already applied to itself
            // before it submits its own — this is where stacking emerges, not in the lookup below.
            for (int i = 0; i < bay.Count; i++)
            {
                _current = i;
                bay[i].SubmitMagnifications(this);
            }
        }

        /// <summary>Convenience factory for the context-construction sites.</summary>
        public static EffectMagnifier ForBay(IReadOnlyList<IModifierCard> bay) => new(bay);

        public void SubmitMagnification(double magnification, MagnificationApplicationRule rule)
        {
            int target = rule switch
            {
                MagnificationApplicationRule.ImmediateRightNeighbor => _current + 1,
                _ => -1,
            };
            if (target < 0 || target >= _bay.Count)
                return;

            _byTarget[target] = (_byTarget.TryGetValue(target, out var existing) ? existing : 1.0) * magnification;
        }

        public double GetMagnification(IModifierCard card)
            => _indexOf.TryGetValue(card, out var idx) && _byTarget.TryGetValue(idx, out var mag) ? mag : 1.0;
    }
}
