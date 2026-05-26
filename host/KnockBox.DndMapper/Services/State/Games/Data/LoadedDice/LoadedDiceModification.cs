using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace KnockBox.DndMapper.Services.State.Games.Data.LoadedDice
{
    // Strategy base for "then X" mutations of a single die's result. The
    // processor calls Apply per-die for each surviving rule, chains the
    // outputs together top-to-bottom, and clamps into [1, sides] after each
    // call so out-of-range parameter values can't produce an impossible face.
    [JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
    [JsonDerivedType(typeof(SetResultModification), "setResult")]
    [JsonDerivedType(typeof(ClampMaxModification), "clampMax")]
    [JsonDerivedType(typeof(ClampMinModification), "clampMin")]
    [JsonDerivedType(typeof(BiasLowerModification), "biasLower")]
    [JsonDerivedType(typeof(BiasHigherModification), "biasHigher")]
    [JsonDerivedType(typeof(RerollOnModification), "rerollOn")]
    public abstract record LoadedDiceModification
    {
        public abstract int Apply(int dieResult, LoadedDiceContext ctx);
    }

    public sealed record SetResultModification(int Value) : LoadedDiceModification
    {
        public override int Apply(int dieResult, LoadedDiceContext ctx) => Value;
    }

    public sealed record ClampMaxModification(int Max) : LoadedDiceModification
    {
        public override int Apply(int dieResult, LoadedDiceContext ctx)
            => dieResult > Max ? Max : dieResult;
    }

    public sealed record ClampMinModification(int Min) : LoadedDiceModification
    {
        public override int Apply(int dieResult, LoadedDiceContext ctx)
            => dieResult < Min ? Min : dieResult;
    }

    // Rolls RerollCount extra dice with the same number of sides and keeps
    // the minimum across the original plus the rerolls. RerollCount==1 is
    // the "disadvantage-like" 2d-take-low pattern.
    public sealed record BiasLowerModification(int RerollCount) : LoadedDiceModification
    {
        public override int Apply(int dieResult, LoadedDiceContext ctx)
        {
            int best = dieResult;
            int rerolls = RerollCount < 1 ? 1 : RerollCount;
            for (int i = 0; i < rerolls; i++)
            {
                int candidate = ctx.RollNewDie(ctx.DiceTermSides);
                if (candidate < best) best = candidate;
            }
            return best;
        }
    }

    public sealed record BiasHigherModification(int RerollCount) : LoadedDiceModification
    {
        public override int Apply(int dieResult, LoadedDiceContext ctx)
        {
            int best = dieResult;
            int rerolls = RerollCount < 1 ? 1 : RerollCount;
            for (int i = 0; i < rerolls; i++)
            {
                int candidate = ctx.RollNewDie(ctx.DiceTermSides);
                if (candidate > best) best = candidate;
            }
            return best;
        }
    }

    // Re-rolls (once) when the original landed on any value in Values. Used
    // for "halflings reroll 1s" style mechanics. Single reroll is final to
    // avoid unbounded loops when the parameter is misconfigured (e.g.
    // Values contains every face).
    public sealed record RerollOnModification(ImmutableHashSet<int> Values) : LoadedDiceModification
    {
        public override int Apply(int dieResult, LoadedDiceContext ctx)
            => Values is { Count: > 0 } && Values.Contains(dieResult)
                ? ctx.RollNewDie(ctx.DiceTermSides)
                : dieResult;
    }
}
