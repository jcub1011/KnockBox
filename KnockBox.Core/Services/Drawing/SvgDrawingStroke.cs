namespace KnockBox.Services.Drawing;

public sealed class SvgDrawingStroke(string color, double strokeSize)
{
    private readonly List<SvgDrawingPoint> _points = [];

    public string Color { get; } = SvgDrawingDocument.NormalizeColor(color, SvgDrawingDocument.DefaultBrushColor);
    public double StrokeSize { get; } = SvgDrawingDocument.NormalizeStrokeSize(strokeSize);
    public IReadOnlyList<SvgDrawingPoint> Points => _points;

    public void AddPoint(double x, double y)
    {
        var point = new SvgDrawingPoint(x, y);
        if (_points.Count > 0 && _points[^1] == point)
        {
            return;
        }

        _points.Add(point);
    }
}
