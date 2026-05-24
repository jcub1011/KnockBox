namespace KnockBox.DndMapper.Services.Logic.Visibility
{
    public enum FogPaintMode { Off, Paint, Erase }

    // Scoped (per-circuit) shared state between HostFogPanel (writer) and
    // MapCanvas (reader). A service rather than a cascaded value because the
    // canvas needs to read the current mode inside JS interop callbacks, not
    // only during re-render, and a service avoids re-render coupling.
    public interface IFogPaintContext
    {
        FogPaintMode Mode { get; }
        int BrushRadius { get; }
        event Action? Changed;
        void Set(FogPaintMode mode, int brushRadius);
    }

    public sealed class FogPaintContext : IFogPaintContext
    {
        public const int MinBrush = 1;
        public const int MaxBrush = 3;

        public FogPaintMode Mode { get; private set; } = FogPaintMode.Off;
        public int BrushRadius { get; private set; } = MinBrush;

        public event Action? Changed;

        public void Set(FogPaintMode mode, int brushRadius)
        {
            // Clamp outside the valid range back to MinBrush so a UI bug or
            // stale state can't leave the canvas with an unusable brush.
            var clamped = brushRadius < MinBrush || brushRadius > MaxBrush ? MinBrush : brushRadius;
            if (Mode == mode && BrushRadius == clamped) return;
            Mode = mode;
            BrushRadius = clamped;
            Changed?.Invoke();
        }
    }
}
