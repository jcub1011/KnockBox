using KnockBox.AlphaChain.Services.Logic.Games.Data;

namespace KnockBox.AlphaChain.Services.Logic.Games.Data.Cards.Library
{
    /// <summary>×3 when the word has more vowels than consonants.</summary>
    public sealed class VowelSurgeCard : MultiplicativeCardBase
    {
        public override ModifierId GetId() => ModifierId.VowelSurge;
        public override string GetName() => "Vowel Surge";
        public override string GetIcon() => "wave";
        public override string GetDescription() => "×3 when your word has more vowels than consonants.";

        public override bool CheckIfTriggered(EngineEvaluationContext context)
            => this.GetVowelIndicies(context).Count() > this.GetConsonantIndicies(context).Count();

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 3.0;
    }

    /// <summary>×1.5 when the word's only vowels are 'A' or 'E'.</summary>
    public sealed class GutturalRoarCard : MultiplicativeCardBase
    {
        public override ModifierId GetId() => ModifierId.GutturalRoar;
        public override string GetName() => "Guttural Roar";
        public override string GetIcon() => "roar";
        public override string GetDescription() => "×1.5 when your word's only vowels are 'A' or 'E'.";

        public override bool CheckIfTriggered(EngineEvaluationContext context)
            => this.GetVowelIndicies(context).All(i => context.Word[i] is 'a' or 'e');

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 1.5;
    }

    /// <summary>×1.5 when the word ends in a vowel.</summary>
    public sealed class PerfectLinkCard : MultiplicativeCardBase
    {
        public override ModifierId GetId() => ModifierId.PerfectLink;
        public override string GetName() => "Perfect Link";
        public override string GetIcon() => "link";
        public override string GetDescription()
            => "×1.5 when your word ends in a vowel — hand the next player an easy letter, pad your own score.";

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
        public override string GetIcon() => "catalyst";
        public override string GetDescription()
            => "Grants 0 points. For every card placed after it, the letters Y, W and H count as both a vowel AND a consonant.";

        protected override double GetMagnitude(EngineEvaluationContext context, IModifierCard self) => 1.0;

        public bool IsVowel(char character) => CatalystVowels.Contains(character);
    }
}
