namespace KnockBox.DndMapper.Helpers
{
    public static class LayerOrderResolver
    {
        /// <summary>
        /// Resolves a layer-shift request (delta) into a concrete target index that the
        /// engine will accept (0 &lt;= index &lt; imageCount). <see cref="int.MaxValue"/>
        /// means "to top" and <see cref="int.MinValue"/> means "to bottom". Returns
        /// <c>null</c> when the operation would be a no-op (empty list or target equals
        /// the current index).
        /// </summary>
        public static int? Resolve(int delta, int currentIndex, int imageCount)
        {
            if (imageCount <= 0) return null;
            int maxIndex = imageCount - 1;

            int target = delta switch
            {
                int.MaxValue => maxIndex,
                int.MinValue => 0,
                _ => currentIndex + delta,
            };
            target = Math.Clamp(target, 0, maxIndex);
            return target == currentIndex ? null : target;
        }
    }
}
