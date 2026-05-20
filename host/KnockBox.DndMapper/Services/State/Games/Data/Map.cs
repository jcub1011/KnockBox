namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed class Map
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public GridConfig Grid { get; set; } = new();
        public List<MapImage> Images { get; } = [];
        public List<Token> Tokens { get; } = [];
        public DateTime CreatedUtc { get; set; }
        public int ListOrder { get; set; }
        public (double X, double Y)? DefaultSpawnPosition { get; set; }
        // v1.x markup overlay (§5.6). Serialized SVG inner markup written by
        // the host's drawing canvas. Null when the host hasn't drawn anything.
        public string? MarkupSvg { get; set; }

        // Packed row-major bitset, length = (WidthCells * HeightCells + 7) / 8.
        // Empty array means "all cells revealed" — the engine verbs allocate on
        // first paint and ClearAllFogAsync resets to [] so the common case stays
        // cheap. The setter is public because the library service assigns the
        // deserialized array back directly during hydrate; at runtime mutation
        // funnels through SetFogged under state.Execute.
        public byte[] FogMask { get; set; } = [];

        public bool IsFogged(int cx, int cy)
        {
            if (FogMask.Length == 0) return false;
            if (cx < 0 || cy < 0 || cx >= Grid.WidthCells || cy >= Grid.HeightCells) return false;
            var bit = cy * Grid.WidthCells + cx;
            var idx = bit >> 3;
            if ((uint)idx >= (uint)FogMask.Length) return false;
            return (FogMask[idx] & (1 << (bit & 7))) != 0;
        }

        public void SetFogged(int cx, int cy, bool fogged)
        {
            if (cx < 0 || cy < 0 || cx >= Grid.WidthCells || cy >= Grid.HeightCells) return;
            EnsureMaskAllocated();
            var bit = cy * Grid.WidthCells + cx;
            var idx = bit >> 3;
            var mask = (byte)(1 << (bit & 7));
            if (fogged) FogMask[idx] |= mask;
            else FogMask[idx] = (byte)(FogMask[idx] & ~mask);
        }

        private void EnsureMaskAllocated()
        {
            if (FogMask.Length != 0) return;
            var bytes = (Grid.WidthCells * Grid.HeightCells + 7) / 8;
            FogMask = bytes > 0 ? new byte[bytes] : [];
        }
    }
}
