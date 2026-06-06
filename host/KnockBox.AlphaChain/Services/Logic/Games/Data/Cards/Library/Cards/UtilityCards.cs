using System.Collections.Immutable;
using KnockBox.AlphaChain.Services.Logic.Games.Data;
using KnockBox.AlphaChain.Services.Logic.Games.Evaluation;
using KnockBox.AlphaChain.Services.State.Games.Data;

namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library
{
    /// <summary>Hides the owner's own input box while typing, in exchange for a flat ×1.8.</summary>
    public sealed class BlindfoldCard : MultiplicativeCardBase, IInputMask
    {
        public override ModifierId GetId() => ModifierId.Blindfold;
        public override string GetName() => "The Blindfold";
        public override string GetDescription(EngineEvaluationContext context)
            => "Hides your own word-input box while you type — no peeking at typos. Reward: ×1.8 on every valid word.";
        protected override string? MagnitudeLabel => "×1.8";
        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 1.8 * GetMagnification(context);

        public bool HidesOwnInput(EngineEvaluationContext context) => true;
    }

    /// <summary>
    /// The Magnifying Glass: a scoring-inert (×1.0) support card that magnifies the effect/magnitude of
    /// the card <i>immediately to its right</i> by ×1.5. It pushes its magnification through the
    /// per-evaluation <see cref="IEffectMagnifier"/>; the right-hand card decides how to scale its own
    /// numbers (additive addend, multiplier factor, Forgery's perceived length, clock deltas, economy
    /// values). Stacking is automatic: a glass folds the magnification already applied to itself into
    /// what it submits, so two glasses in series compound onto the one neighbor (1.5 × 1.5 = 2.25).
    /// </summary>
    public sealed class MagnifyingGlassCard : MultiplicativeCardBase
    {
        /// <summary>The base factor a single glass magnifies its right-hand neighbor by.</summary>
        public const double BaseMagnification = 1.5;

        public override ModifierId GetId() => ModifierId.MagnifyingGlass;
        public override string GetName() => "Magnifying Glass";
        public override string GetDescription(EngineEvaluationContext context)
            => "Grants 0 points. Magnifies the effect of the card immediately to its right by ×1.5. Stacks: glasses in series compound (two → ×2.25).";
        protected override string? MagnitudeLabel => "FX";

        // Inert in the scoring fold — its whole job is to magnify its neighbor, not to score.
        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 1.0;

        public override System.Collections.Immutable.ImmutableArray<CardChip> GetChips(EngineEvaluationContext context)
            => [new CardChip("×1.5 →", CardChips.Effect)];

        public override void SubmitMagnifications(IEffectMagnifier magnifier)
        {
            // Fold the magnification already applied to THIS glass (a glass on my left) into what I emit,
            // so stacking compounds without this card or the service knowing about neighbors.
            double own = magnifier.GetMagnification(this);
            magnifier.SubmitMagnification(BaseMagnification * own, MagnificationApplicationRule.ImmediateRightNeighbor);
        }
    }

    /// <summary>Lets the owner ignore the Succession (chain) rule — once per era. The charge re-arms at
    /// era start and is spent only when a chain-breaking word is actually accepted (see RoundState).</summary>
    public sealed class WildcardCard : MultiplicativeCardBase, ISuccessionExemption, IContributesRoomServices
    {
        public override ModifierId GetId() => ModifierId.Wildcard;
        public override string GetName() => "The Wildcard";
        public override string GetDescription(EngineEvaluationContext context)
            => "Grants 0 points. Once per era, one of your words may ignore the Succession rule — it need not begin with the last letter of the previous word.";
        protected override string? MagnitudeLabel => "FX";
        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 1.0;

        // The exemption is available only while the owner hasn't spent it this era; the FSM consumes it
        // (via IWildcardGuard.Consume) only when an accepted word actually relied on the bypass.
        public bool IgnoresSuccession(EngineEvaluationContext context)
        {
            var owner = context.GetPlayer(context.PlayerIndex);
            return owner is not null && context.Service<IWildcardGuard>()?.HasConsumed(owner) != true;
        }

        public IEnumerable<RoomServiceDescriptor> GetRoomServices()
            => [new(typeof(IWildcardGuard), static _ => new WildcardEraGuard())];
    }

    /// <summary>
    /// The IRS Agent: inert in the pipeline, but overrides the owner's own Zero-Point Tax — they keep
    /// a flat score (0) on a banned-letter word and no opponent's siphon collects from it.
    /// </summary>
    public sealed class IrsAgentCard : MultiplicativeCardBase, IOwnTaxPolicy
    {
        public override ModifierId GetId() => ModifierId.IrsAgent;
        public override string GetName() => "The IRS Agent";
        public override string GetDescription(EngineEvaluationContext context)
            => "Grants 0 points. When YOUR word is hit by the Zero-Point Tax, no opponent's Tax Collector collects a thing.";
        protected override string? MagnitudeLabel => "FX";

        // Inert in the scoring pipeline; its effect is the tax override below.
        public override bool CheckIfTriggered(EngineEvaluationContext context) => false;

        public int GetTaxedScore(EngineEvaluationContext context, int wouldBeScore) => 0;
        public bool SuppressesSiphonBounty => true;
    }

    /// <summary>
    /// The Prism: inert in the pipeline, but on a failed/typo submission it refills the owner's shot
    /// clock to full (once per era) instead of letting it tick down — the essential pairing with The
    /// Blindfold.
    /// </summary>
    public sealed class PrismCard : MultiplicativeCardBase, IContributesRoomServices
    {
        public override ModifierId GetId() => ModifierId.Prism;
        public override string GetName() => "The Prism";
        public override string GetDescription(EngineEvaluationContext context)
            => "Grants 0 points. If your word is a typo or fails validation, your shot clock resets to full — once per era — instead of ticking away.";
        protected override string? MagnitudeLabel => "FX";
        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 1.0;

        public override EngineEvaluationContext OnValidationFailed(EngineEvaluationContext context, IModifierCard self)
        {
            // TryConsume returns true at most once per era (and arms the once-per-era guard); the
            // guard re-arms on the owner's next era-start via IPrismGuard.OnEraStarted.
            var owner = context.GetPlayer(context.PlayerIndex);
            if (owner is not null && context.Service<IPrismGuard>()?.TryConsume(owner) == true)
                context.Service<IShotClockService>()?.RefillToFull(owner);
            return context;
        }

        public IEnumerable<RoomServiceDescriptor> GetRoomServices()
            => [new(typeof(IPrismGuard), static _ => new PrismEraGuard())];
    }

    /// <summary>
    /// The Titanium Mirror: its scoring factor is the owner's live shield multiplier (starts at a
    /// passive ×1.0), and it blocks and reflects incoming automated attacks, decaying that multiplier
    /// by a fixed step per block — possibly below 1.0 into a scoring burden carried across eras.
    /// </summary>
    public sealed class TitaniumMirrorCard : MultiplicativeCardBase, IAttackInterceptor, IContributesRoomServices
    {
        /// <summary>The shield's per-block decay step.</summary>
        public const double DecayPerBlock = 0.1;

        public override ModifierId GetId() => ModifierId.TitaniumMirror;
        public override string GetName() => "The Titanium Mirror";
        public override string GetDescription(EngineEvaluationContext context)
            => "Passive ×1.0. Automatically blocks and reflects incoming attacks (time shaves, point drains, letter hijacks) back at their source — but loses 0.1× per block, carrying its decay across eras until discarded.";
        protected override string? MagnitudeLabel => "shield";

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self)
            => Multiplier(context) * GetMagnification(context);

        public bool TryIntercept(AlphaChainPlayerState owner, IModifierCard self, IServiceProvider services)
        {
            (services.GetService(typeof(IShieldService)) as IShieldService)?.Decay(owner, DecayPerBlock);
            return true;
        }

        /// <summary>Appends the live shield factor (e.g. "×0.7") to the base chips so the player can see
        /// how far the mirror has decayed; absent outside a live context where the owner can't be resolved.</summary>
        public override ImmutableArray<CardChip> GetChips(EngineEvaluationContext context)
        {
            var chips = base.GetChips(context);
            var owner = context.GetPlayer(context.PlayerIndex);
            var shield = context.Service<IShieldService>();
            return owner is not null && shield is not null
                ? chips.Add(new CardChip($"×{shield.GetMultiplier(owner):0.0}", CardChips.Live))
                : chips;
        }

        public IEnumerable<RoomServiceDescriptor> GetRoomServices()
            => [new(typeof(IShieldService), static _ => new ShieldService())];

        private static double Multiplier(EngineEvaluationContext context)
        {
            var owner = context.GetPlayer(context.PlayerIndex);
            var shield = context.Service<IShieldService>();
            return owner is not null && shield is not null ? shield.GetMultiplier(owner) : 1.0;
        }
    }
}
