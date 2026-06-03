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
        public override string GetIcon() => "blindfold";
        public override string GetDescription()
            => "Hides your own word-input box while you type — no peeking at typos. Reward: ×1.8 on every valid word.";
        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 1.8;

        public bool HidesOwnInput(EngineEvaluationContext context) => true;
    }

    /// <summary>Lets the owner ignore the Succession (chain) rule.</summary>
    public sealed class WildcardCard : MultiplicativeCardBase, ISuccessionExemption
    {
        public override ModifierId GetId() => ModifierId.Wildcard;
        public override string GetName() => "The Wildcard";
        public override string GetIcon() => "wildcard";
        public override string GetDescription()
            => "Grants 0 points. Your words ignore the Succession rule — they need not begin with the last letter of the previous word.";
        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 1.0;

        public bool IgnoresSuccession(EngineEvaluationContext context) => true;
    }

    /// <summary>
    /// The IRS Agent: inert in the pipeline, but overrides the owner's own Zero-Point Tax — they keep
    /// a flat score (0) on a banned-letter word and no opponent's siphon collects from it.
    /// </summary>
    public sealed class IrsAgentCard : MultiplicativeCardBase, IOwnTaxPolicy
    {
        public override ModifierId GetId() => ModifierId.IrsAgent;
        public override string GetName() => "The IRS Agent";
        public override string GetIcon() => "irs";
        public override string GetDescription()
            => "Grants 0 points. When YOUR word is hit by the Zero-Point Tax, no opponent's Tax Collector collects a thing.";

        // Inert in the scoring pipeline; its effect is the tax override below.
        public override bool CheckIfTriggered(EngineEvaluationContext context) => false;

        public int GetTaxedScore(EngineEvaluationContext context, int wouldBeScore) => 0;
        public bool SuppressesSiphonBounty => true;
    }

    /// <summary>
    /// The Prism: inert in the pipeline, but on a failed/typo submission it refills the owner's shot
    /// clock to full (once per turn) instead of letting it tick down — the essential pairing with The
    /// Blindfold.
    /// </summary>
    public sealed class PrismCard : MultiplicativeCardBase
    {
        public override ModifierId GetId() => ModifierId.Prism;
        public override string GetName() => "The Prism";
        public override string GetIcon() => "prism";
        public override string GetDescription()
            => "Grants 0 points. If your word is a typo or fails validation, your shot clock resets to full — once per turn — instead of ticking away.";
        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 1.0;

        public override EngineEvaluationContext OnValidationFailed(EngineEvaluationContext context, IModifierCard self)
        {
            var owner = context.GetPlayer(context.PlayerIndex);
            if (owner is { PrismUsedThisTurn: false })
            {
                context.Service<IShotClockService>()?.RefillToFull(owner);
                owner.PrismUsedThisTurn = true;
            }
            return context;
        }
    }

    /// <summary>
    /// The Titanium Mirror: its scoring factor is the owner's live shield multiplier (starts at a
    /// passive ×1.0), and it blocks and reflects incoming automated attacks, decaying that multiplier
    /// by a fixed step per block — possibly below 1.0 into a scoring burden carried across eras.
    /// </summary>
    public sealed class TitaniumMirrorCard : MultiplicativeCardBase, IAttackInterceptor
    {
        /// <summary>The shield's per-block decay step.</summary>
        public const double DecayPerBlock = 0.1;

        public override ModifierId GetId() => ModifierId.TitaniumMirror;
        public override string GetName() => "The Titanium Mirror";
        public override string GetIcon() => "titanium-mirror";
        public override string GetDescription()
            => "Passive ×1.0. Automatically blocks and reflects incoming attacks (time shaves, point drains, letter hijacks) back at their source — but loses 0.1× per block, carrying its decay across eras until discarded.";

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self)
            => context.GetShieldMultiplier(context.PlayerIndex);

        public bool TryIntercept(AlphaChainPlayerState owner, IModifierCard self)
        {
            owner.ShieldMultiplier = Math.Max(0.0, owner.ShieldMultiplier - DecayPerBlock);
            return true;
        }
    }
}
