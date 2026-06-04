using KnockBox.AlphaChain.Services.Logic.Games.Data;

namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library
{
    /// <summary>×3 when the word has more vowels than consonants.</summary>
    public sealed class VowelSurgeCard : MultiplicativeCardBase
    {
        public override ModifierId GetId() => ModifierId.VowelSurge;
        public override string GetName() => "Vowel Surge";
        public override string GetDescription() => "×3 when your word has more vowels than consonants.";
        public override string? GetMagnitudeLabel() => "×3";

        public override bool CheckIfTriggered(EngineEvaluationContext context)
            => this.GetVowelIndicies(context).Count() > this.GetConsonantIndicies(context).Count();

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 3.0;
    }

    /// <summary>×1.5 when the word's only vowels are 'A' or 'E'.</summary>
    public sealed class GutturalRoarCard : MultiplicativeCardBase
    {
        public override ModifierId GetId() => ModifierId.GutturalRoar;
        public override string GetName() => "Guttural Roar";
        public override string GetDescription() => "×1.5 when your word's only vowels are 'A' or 'E'.";
        public override string? GetMagnitudeLabel() => "×1.5";

        public override bool CheckIfTriggered(EngineEvaluationContext context)
            => this.GetVowelIndicies(context).All(i => context.Word[i] is 'a' or 'e');

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 1.5;
    }

    /// <summary>×1.5 when the word ends in a vowel.</summary>
    public sealed class PerfectLinkCard : MultiplicativeCardBase
    {
        public override ModifierId GetId() => ModifierId.PerfectLink;
        public override string GetName() => "Perfect Link";
        public override string GetDescription()
            => "×1.5 when your word ends in a vowel — hand the next player an easy letter, pad your own score.";
        public override string? GetMagnitudeLabel() => "×1.5";

        public override bool CheckIfTriggered(EngineEvaluationContext context)
            => context.Word.Length > 0 && this.GetVowelIndicies(context).Contains(context.Word.Length - 1);

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 1.5;
    }

    /// <summary>
    /// The Catalyst: a 0-point utility (×1.0) that, for every card evaluated <i>after</i> it in the
    /// bay, makes Y, W and H count as a vowel in addition to their normal consonant role — the
    /// canonical capability-interface card. It overrides only vowel classification; consonant
    /// classification is unchanged (Y/W/H are already consonants), so those letters count as both.
    /// </summary>
    public sealed class CatalystCard : MultiplicativeCardBase, IVowelChecker
    {
        private static readonly System.Buffers.SearchValues<char> CatalystVowels =
            System.Buffers.SearchValues.Create("aeiouywh");

        public override ModifierId GetId() => ModifierId.Catalyst;
        public override string GetName() => "The Catalyst";
        public override string GetDescription()
            => "Grants 0 points. For every card placed after it, the letters Y, W and H count as both a vowel AND a consonant.";
        public override string? GetMagnitudeLabel() => "FX";

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 1.0;

        public bool IsVowel(char character) => CatalystVowels.Contains(character);
    }

    // ── Length-scoring cards ────────────────────────────────────────────────────
    // These read the Forgery-perceived letter count (GetEffectiveLetterCount) rather than
    // context.Word.Length, so a Forgery placed before them doubles what they perceive. They are
    // classes (not CommonModifier) because their triggers need the bay-walking `self` argument.

    /// <summary>+1 per letter, +2 per letter when the word is 7+ letters.</summary>
    public sealed class VanillaCard : AdditiveCardBase
    {
        public override ModifierId GetId() => ModifierId.Vanilla;
        public override string GetName() => "Vanilla";
        public override string GetDescription()
            => "Adds +1 for every letter in your word. +2 if the word is 7+ characters.";
        public override string? GetMagnitudeLabel() => "+1-2 / ltr";

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self)
        {
            int letters = self.GetEffectiveLetterCount(context);
            return letters < 7 ? letters : letters * 2;
        }
    }

    /// <summary>+1 per letter once the word reaches 6 letters.</summary>
    public sealed class BrickLayerCard : AdditiveCardBase
    {
        public override ModifierId GetId() => ModifierId.BrickLayer;
        public override string GetName() => "Brick Layer";
        public override string GetDescription() => "Adds +1 per letter when your word is 6 letters or longer.";
        public override string? GetMagnitudeLabel() => "+1 / ltr";

        public override bool CheckIfTriggered(EngineEvaluationContext context)
            => this.GetEffectiveLetterCount(context) >= 6;

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self)
            => self.GetEffectiveLetterCount(context);
    }

    /// <summary>×3 when the word is 8 letters or longer.</summary>
    public sealed class TheArchitectCard : MultiplicativeCardBase
    {
        public override ModifierId GetId() => ModifierId.TheArchitect;
        public override string GetName() => "The Architect";
        public override string GetDescription() => "×3 when your word is 8 letters or longer.";
        public override string? GetMagnitudeLabel() => "×3";

        public override bool CheckIfTriggered(EngineEvaluationContext context)
            => this.GetEffectiveLetterCount(context) >= 8;

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 3.0;
    }

    /// <summary>×5 when the word is 10 letters or longer.</summary>
    public sealed class SesquipedalianCard : MultiplicativeCardBase
    {
        public override ModifierId GetId() => ModifierId.Sesquipedalian;
        public override string GetName() => "Sesquipedalian";
        public override string GetDescription()
            => "×5 when your word is 10 letters or longer. Clamped to the max word score.";
        public override string? GetMagnitudeLabel() => "×5";

        public override bool CheckIfTriggered(EngineEvaluationContext context)
            => this.GetEffectiveLetterCount(context) >= 10;

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 5.0;
    }

    /// <summary>
    /// Speedracer: when the word is longer than 6 letters, a "submit fast" multiplier of
    /// 1 / (remaining / total), capped at half the (perceived) letter count via integer division
    /// (a 9-letter word caps at ×4). Reads the Forgery-perceived count for both the trigger and the cap.
    /// </summary>
    public sealed class SpeedracerCard : MultiplicativeCardBase
    {
        public override ModifierId GetId() => ModifierId.Speedracer;
        public override string GetName() => "Speedracer";
        public override string GetDescription()
            => "When your word is longer than 6 letters, you get a multiplier (1 / ([remaining time] / [total time])). Capped at half your letter count.";
        public override string? GetMagnitudeLabel() => "≤ × (ltr/2)";

        public override bool CheckIfTriggered(EngineEvaluationContext context)
            => this.GetEffectiveLetterCount(context) > 6;

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self)
        {
            int cap = self.GetEffectiveLetterCount(context) / 2;
            if (context.RemainingShotClockDuration <= 0)
                return cap;
            return Math.Min(1.0 / (context.RemainingShotClockDuration / context.ShotClockDuration), cap);
        }
    }

    /// <summary>
    /// The Blueprint: +3 per letter when the word is at least as long as the previously submitted word
    /// (the immediately preceding play in the chain). With no prior word yet, it always pays out. The
    /// current word's length is Forgery-perceived; the previous word's length is its actual length.
    /// </summary>
    public sealed class TheBlueprintCard : AdditiveCardBase
    {
        public override ModifierId GetId() => ModifierId.TheBlueprint;
        public override string GetName() => "The Blueprint";
        public override string GetDescription()
            => "Adds +3 per letter when your word is at least as long as the previously submitted word.";
        public override string? GetMagnitudeLabel() => "+3 / ltr";

        public override bool CheckIfTriggered(EngineEvaluationContext context)
            => context.SubmissionHistory.IsEmpty
                || this.GetEffectiveLetterCount(context) >= context.SubmissionHistory[^1].Word.Length;

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self)
            => 3.0 * self.GetEffectiveLetterCount(context);
    }

    /// <summary>
    /// Try Hard: ×(1 + 0.1 per letter beyond 6) — a 7-letter word is ×1.1, an 8-letter word ×1.2, and
    /// so on. Reads the Forgery-perceived letter count for both the trigger and the factor.
    /// </summary>
    public sealed class TryHardCard : MultiplicativeCardBase
    {
        /// <summary>Letters beyond this count each add 0.1 to the multiplier.</summary>
        public const int BaseLength = 6;

        public override ModifierId GetId() => ModifierId.TryHard;
        public override string GetName() => "Try Hard";
        public override string GetDescription()
            => "×1.1 at 7 letters, +0.1 for each letter beyond that (8 letters ×1.2, 9 letters ×1.3, …).";
        public override string? GetMagnitudeLabel() => "×1.1+";

        public override bool CheckIfTriggered(EngineEvaluationContext context)
            => this.GetEffectiveLetterCount(context) > BaseLength;

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self)
            => 1.0 + 0.1 * (self.GetEffectiveLetterCount(context) - BaseLength);
    }

    /// <summary>
    /// Forgery: a 0-point utility (×1.0) that makes every card placed after it perceive double the
    /// word's letter count — flowing into their length conditionals and per-letter magnitudes via
    /// <see cref="ModifierCapabilityExtensions.GetEffectiveLetterCount"/>. The evaluator's base
    /// word-length seed is untouched, and per-character cards (Consonant Crunch, Vocal Vowels, Vowel
    /// Surge, Guttural Roar, Letter Hoarder, Perfect Link, Double Down) read actual characters and are
    /// unaffected.
    /// </summary>
    public sealed class ForgeryCard : MultiplicativeCardBase, ILetterCountModifier
    {
        public override ModifierId GetId() => ModifierId.Forgery;
        public override string GetName() => "Forgery";
        public override string GetDescription()
            => "Grants 0 points. Every card placed after it treats your word as having double the letters (length-based cards only).";
        public override string? GetMagnitudeLabel() => "FX";

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 1.0;

        public int LetterCountMultiplier => 2;
    }
}
