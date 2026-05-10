namespace KnockBox.DndMapper.Services.Logic.Games
{
    internal static class DefaultColorPalette
    {
        private static readonly string[] _palette =
        [
            "#1f77b4", "#ff7f0e", "#2ca02c", "#d62728",
            "#9467bd", "#8c564b", "#e377c2", "#17becf"
        ];

        public const string Neutral = "#888";

        public static string ForPlayerSlot(int slotIndex)
            => _palette[((slotIndex % _palette.Length) + _palette.Length) % _palette.Length];
    }
}
