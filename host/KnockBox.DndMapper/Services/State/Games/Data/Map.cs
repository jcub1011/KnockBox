using System.Collections.Immutable;
using System.Runtime.InteropServices;

namespace KnockBox.DndMapper.Services.State.Games.Data
{
    public sealed record Map
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public GridConfig Grid { get; init; } = new();
        public ImmutableArray<MapImage> Images { get; init; } = ImmutableArray<MapImage>.Empty;
        public ImmutableArray<Token> Tokens { get; init; } = ImmutableArray<Token>.Empty;
        public DateTime CreatedUtc { get; init; }
        public int ListOrder { get; init; }
        public (double X, double Y)? DefaultSpawnPosition { get; init; }
        // v1.x markup overlay (§5.6). Serialized SVG inner markup written by
        // the host's drawing canvas. Null when the host hasn't drawn anything.
        public string? MarkupSvg { get; init; }

        // Packed row-major bitset, length = (WidthCells * HeightCells + 7) / 8.
        // Default (empty) means "all cells revealed" — the engine verbs build
        // a fresh mask on first paint and ClearAllFogAsync resets to default
        // so the common case stays cheap.
        public ImmutableArray<byte> FogMask { get; init; } = ImmutableArray<byte>.Empty;

        // Monotonic version counters paired with FogMask and Images. Bumped by
        // engine verbs on every actual mutation so per-frame consumers (canvas
        // memoization) can skip rebuild work when nothing relevant changed.
        public int FogVersion { get; init; }
        public int ImagesVersion { get; init; }

        // Narrower companion to ImagesVersion that only bumps when an image
        // is added, removed, or its Locked flag toggles — never on transform,
        // opacity, hidden, name, or share-token mutations.
        public int ImagesMembershipVersion { get; init; }

        public bool IsFogged(int cx, int cy)
        {
            var mask = FogMask;
            if (mask.IsDefaultOrEmpty) return false;
            if (cx < 0 || cy < 0 || cx >= Grid.WidthCells || cy >= Grid.HeightCells) return false;
            var bit = cy * Grid.WidthCells + cx;
            var idx = bit >> 3;
            if ((uint)idx >= (uint)mask.Length) return false;
            return (mask[idx] & (1 << (bit & 7))) != 0;
        }

        // Functional cell-flip. Returns this when the bit was already in the
        // target state or the cell is out of bounds; otherwise returns a new
        // Map with the updated mask and a bumped FogVersion.
        public Map WithCellFogged(int cx, int cy, bool fogged)
        {
            if (cx < 0 || cy < 0 || cx >= Grid.WidthCells || cy >= Grid.HeightCells) return this;

            var bits = (long)Grid.WidthCells * Grid.HeightCells;
            var byteCount = (int)((bits + 7) / 8);
            if (byteCount <= 0) return this;

            var workingMask = FogMask.IsDefaultOrEmpty ? new byte[byteCount] : FogMask.ToArray();
            var bit = cy * Grid.WidthCells + cx;
            var idx = bit >> 3;
            var maskByte = (byte)(1 << (bit & 7));
            var before = workingMask[idx];
            byte after = fogged ? (byte)(before | maskByte) : (byte)(before & ~maskByte);
            if (after == before) return this;
            workingMask[idx] = after;
            return this with
            {
                FogMask = ImmutableCollectionsMarshal.AsImmutableArray(workingMask),
                FogVersion = FogVersion + 1,
            };
        }

        // Test-friendly mutating shim. Records are immutable so the only
        // sensible signature is "return the new instance"; in-place tests
        // should assign the return value back. Returns true when the mask
        // changed, false on no-op (out-of-bounds or bit already in state).
        public bool TryWithCellFogged(int cx, int cy, bool fogged, out Map result)
        {
            var next = WithCellFogged(cx, cy, fogged);
            result = next;
            return !ReferenceEquals(next, this);
        }
    }
}
