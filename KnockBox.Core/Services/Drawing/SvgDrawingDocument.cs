using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace KnockBox.Services.Drawing;

public sealed partial class SvgDrawingDocument
{
    private static readonly Regex ColorRegex = ColorPattern();
    private readonly List<SvgDrawingStroke> _strokes = [];
    private SvgDrawingStroke? _activeStroke;

    public const string DefaultBrushColor = "#111827";
    public const string DefaultBackgroundColor = "#FFFFFF";
    public const double MinimumBrushSize = 1d;
    public const double MaximumBrushSize = 64d;

    public IReadOnlyList<SvgDrawingStroke> Strokes => _strokes;
    public bool CanUndo => _strokes.Count > 0;

    public SvgDrawingStroke BeginStroke(double x, double y, string color, double strokeSize)
    {
        var stroke = new SvgDrawingStroke(color, strokeSize);
        stroke.AddPoint(x, y);
        _strokes.Add(stroke);
        _activeStroke = stroke;
        return stroke;
    }

    public bool AppendPoint(double x, double y)
    {
        if (_activeStroke is null)
        {
            return false;
        }

        _activeStroke.AddPoint(x, y);
        return true;
    }

    public bool EndStroke()
    {
        if (_activeStroke is null)
        {
            return false;
        }

        _activeStroke = null;
        return true;
    }

    public bool Undo()
    {
        if (_strokes.Count == 0)
        {
            return false;
        }

        if (ReferenceEquals(_activeStroke, _strokes[^1]))
        {
            _activeStroke = null;
        }

        _strokes.RemoveAt(_strokes.Count - 1);
        return true;
    }

    public string ExportSvg(double width, double height, string backgroundColor)
    {
        var normalizedWidth = Math.Max(1d, width);
        var normalizedHeight = Math.Max(1d, height);
        var normalizedBackground = NormalizeColor(backgroundColor, DefaultBackgroundColor);
        var svgNamespace = XNamespace.Get("http://www.w3.org/2000/svg");

        var svg = new XElement(svgNamespace + "svg",
            new XAttribute("version", "1.1"),
            new XAttribute("width", FormatNumber(normalizedWidth)),
            new XAttribute("height", FormatNumber(normalizedHeight)),
            new XAttribute("viewBox", FormattableString.Invariant($"0 0 {FormatNumber(normalizedWidth)} {FormatNumber(normalizedHeight)}")),
            new XElement(svgNamespace + "rect",
                new XAttribute("width", "100%"),
                new XAttribute("height", "100%"),
                new XAttribute("fill", normalizedBackground)));

        foreach (var stroke in _strokes)
        {
            if (stroke.Points.Count == 0)
            {
                continue;
            }

            if (stroke.Points.Count == 1)
            {
                var point = stroke.Points[0];
                svg.Add(new XElement(svgNamespace + "circle",
                    new XAttribute("cx", FormatNumber(point.X)),
                    new XAttribute("cy", FormatNumber(point.Y)),
                    new XAttribute("r", FormatNumber(stroke.StrokeSize / 2d)),
                    new XAttribute("fill", stroke.Color)));
                continue;
            }

            svg.Add(new XElement(svgNamespace + "polyline",
                new XAttribute("points", string.Join(' ', stroke.Points.Select(point => $"{FormatNumber(point.X)},{FormatNumber(point.Y)}"))),
                new XAttribute("fill", "none"),
                new XAttribute("stroke", stroke.Color),
                new XAttribute("stroke-width", FormatNumber(stroke.StrokeSize)),
                new XAttribute("stroke-linecap", "round"),
                new XAttribute("stroke-linejoin", "round")));
        }

        var document = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), svg);
        return document.ToString(SaveOptions.DisableFormatting);
    }

    public static string NormalizeColor(string? color, string fallback)
    {
        var normalizedFallback = ColorRegex.IsMatch(fallback) ? fallback.ToUpperInvariant() : DefaultBrushColor;
        if (string.IsNullOrWhiteSpace(color))
        {
            return normalizedFallback;
        }

        var trimmed = color.Trim();
        return ColorRegex.IsMatch(trimmed) ? trimmed.ToUpperInvariant() : normalizedFallback;
    }

    public static double NormalizeStrokeSize(double strokeSize)
    {
        if (double.IsNaN(strokeSize) || double.IsInfinity(strokeSize))
        {
            return MinimumBrushSize;
        }

        return Math.Clamp(strokeSize, MinimumBrushSize, MaximumBrushSize);
    }

    private static string FormatNumber(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    [GeneratedRegex("^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.CultureInvariant)]
    private static partial Regex ColorPattern();
}
