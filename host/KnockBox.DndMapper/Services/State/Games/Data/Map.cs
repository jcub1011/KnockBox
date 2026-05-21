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
        // cheap. Setter is internal so the bit-helper invariants stay enforceable
        // at the assembly boundary; same-assembly callers (library hydrate +
        // engine verbs) assign the array directly, external code routes through
        // SetFogged. At runtime mutation funnels through SetFogged under
        // state.Execute.
        public byte[] FogMask { get; internal set; } = [];

        // Monotonic version counters paired with FogMask and Images. Bumped by
        // engine verbs on every actual mutation so per-frame consumers (canvas
        // memoization) can skip rebuild work when nothing relevant changed.
        // Internal setter so same-assembly hydration paths can seed/bump
        // explicitly without routing through SetFogged.
        public int FogVersion { get; internal set; }
        public int ImagesVersion { get; internal set; }

        // Narrower companion to ImagesVersion that only bumps when an image
        // is added, removed, or its Locked flag toggles — never on transform,
        // opacity, hidden, name, or share-token mutations. MapCanvas's
        // JS-side image-drag module only cares about (id, locked) set
        // membership, so it uses this counter to skip the marshal when
        // pure transform edits fire.
        public int ImagesMembershipVersion { get; internal set; }

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
            var before = FogMask[idx];
            byte after = fogged ? (byte)(before | mask) : (byte)(before & ~mask);
            if (after == before) return;
            FogMask[idx] = after;
            FogVersion++;
        }

        private void EnsureMaskAllocated()
        {
            if (FogMask.Length != 0) return;
            // (long) cast prevents int overflow on absurdly large grids; the
            // result is bounded by realistic map sizes and fits in int.
            var bytes = (int)(((long)Grid.WidthCells * Grid.HeightCells + 7) / 8);
            FogMask = bytes > 0 ? new byte[bytes] : [];
        }
    }
}
