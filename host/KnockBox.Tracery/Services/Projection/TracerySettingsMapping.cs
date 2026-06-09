using KnockBox.Tracery.Contracts;
using KnockBox.Tracery.Models;
using KnockBox.WordService.Contracts;

namespace KnockBox.Tracery.Services.Projection
{
    /// <summary>
    /// Maps between the server-authoritative <see cref="TracerySettings"/> (which keeps the
    /// playtest tuning tables and the WordService <see cref="WordPoolMode"/>) and the wire
    /// <see cref="TracerySettingsView"/> the client edits/displays. The view carries only the
    /// host-editable knobs; applying it preserves every server-only field on the existing record.
    /// </summary>
    internal static class TracerySettingsMapping
    {
        public static TracerySettingsView ToView(this TracerySettings s) => new()
        {
            Mode = s.Mode,
            SearchListSize = s.SearchListSize,
            SearchPlacementBonusUnit = s.SearchPlacementBonusUnit,
            GridWidth = s.GridWidth,
            GridHeight = s.GridHeight,
            MinWordLength = s.MinWordLength,
            GenerationDictionary = ToPool(s.GenerationDictionary),
            ValidationDictionary = ToPool(s.ValidationDictionary),
            TotalRounds = s.TotalRounds,
            RoundTimerSeconds = (int)s.RoundTimer.TotalSeconds,
            TransitionSeconds = (int)s.TransitionDuration.TotalSeconds,
            IntermissionSeconds = (int)s.IntermissionDuration.TotalSeconds,
            UniqueFindBonusEnabled = s.UniqueFindBonusEnabled,
            UniqueFindMultiplier = s.UniqueFindMultiplier,
            RareLetterBonusEnabled = s.RareLetterBonusEnabled,
        };

        /// <summary>
        /// Returns a copy of <paramref name="s"/> with the host-editable fields replaced from
        /// <paramref name="v"/>, leaving the server-only tuning tables untouched.
        /// <c>TraceryGameState.UpdateSettings</c> runs <c>Normalize()</c> afterwards.
        /// </summary>
        public static TracerySettings Apply(this TracerySettings s, TracerySettingsView v) => s with
        {
            Mode = v.Mode,
            SearchListSize = v.SearchListSize,
            SearchPlacementBonusUnit = v.SearchPlacementBonusUnit,
            GridWidth = v.GridWidth,
            GridHeight = v.GridHeight,
            MinWordLength = v.MinWordLength,
            GenerationDictionary = ToMode(v.GenerationDictionary),
            ValidationDictionary = ToMode(v.ValidationDictionary),
            TotalRounds = v.TotalRounds,
            RoundTimer = TimeSpan.FromSeconds(Math.Max(0, v.RoundTimerSeconds)),
            TransitionDuration = TimeSpan.FromSeconds(v.TransitionSeconds),
            IntermissionDuration = TimeSpan.FromSeconds(v.IntermissionSeconds),
            UniqueFindBonusEnabled = v.UniqueFindBonusEnabled,
            UniqueFindMultiplier = v.UniqueFindMultiplier,
            RareLetterBonusEnabled = v.RareLetterBonusEnabled,
        };

        private static TraceryWordPool ToPool(WordPoolMode m) => m switch
        {
            WordPoolMode.NytStandard => TraceryWordPool.NytStandard,
            WordPoolMode.ReducedDictionary => TraceryWordPool.ReducedDictionary,
            _ => TraceryWordPool.FullDictionary,
        };

        private static WordPoolMode ToMode(TraceryWordPool p) => p switch
        {
            TraceryWordPool.NytStandard => WordPoolMode.NytStandard,
            TraceryWordPool.ReducedDictionary => WordPoolMode.ReducedDictionary,
            _ => WordPoolMode.FullDictionary,
        };
    }
}
