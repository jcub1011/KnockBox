using System.Text;
using System.Text.RegularExpressions;
using Jint;
using Jint.Native;

namespace KnockBox.CoreTests.Unit;

[TestClass]
public partial class SvgCompressionTests
{
    private static string? _jsSource;

    /// <summary>
    /// Loads svgDrawingCanvas.js with ES module syntax stripped so Jint can execute it.
    /// </summary>
    private static string GetJsSource()
    {
        if (_jsSource is not null) return _jsSource;

        var projectDir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", ".."));
        var jsPath = Path.Combine(projectDir,
            "KnockBox.Core", "wwwroot", "js", "svgDrawingCanvas.js");

        var raw = File.ReadAllText(jsPath);

        // Strip ES module export keywords so Jint can parse as a plain script.
        raw = ExportFunctionRegex().Replace(raw, "function ");
        raw = ExportConstRegex().Replace(raw, "const ");
        raw = raw.Replace("export {", "// export {");

        _jsSource = raw;
        return _jsSource;
    }

    /// <summary>
    /// Creates a Jint engine with the drawing canvas functions loaded.
    /// </summary>
    private static Engine CreateEngine()
    {
        var engine = new Engine();
        engine.Execute(GetJsSource());
        return engine;
    }

    // ----- Path data generators -----

    /// <summary>
    /// Generates a spiral with many closely-spaced points.
    /// </summary>
    private static List<(double X, double Y)> GenerateSpiral(
        double cx, double cy, int pointCount, double maxRadius, double revolutions)
    {
        var points = new List<(double, double)>(pointCount);
        for (var i = 0; i < pointCount; i++)
        {
            var t = (double)i / (pointCount - 1);
            var angle = t * revolutions * 2 * Math.PI;
            var radius = t * maxRadius;
            var x = Math.Round((cx + radius * Math.Cos(angle)) * 100) / 100;
            var y = Math.Round((cy + radius * Math.Sin(angle)) * 100) / 100;
            points.Add((x, y));
        }
        return points;
    }

    /// <summary>
    /// Generates a sine-wave path with varying frequency.
    /// </summary>
    private static List<(double X, double Y)> GenerateSineWave(
        double startX, double startY, int pointCount, double length, double amplitude, double frequency)
    {
        var points = new List<(double, double)>(pointCount);
        for (var i = 0; i < pointCount; i++)
        {
            var t = (double)i / (pointCount - 1);
            var x = Math.Round((startX + t * length) * 100) / 100;
            var y = Math.Round((startY + amplitude * Math.Sin(t * frequency * 2 * Math.PI)) * 100) / 100;
            points.Add((x, y));
        }
        return points;
    }

    /// <summary>
    /// Generates a jagged path with small random perturbations on a baseline.
    /// </summary>
    private static List<(double X, double Y)> GenerateJaggedPath(
        double startX, double startY, int pointCount, double length, double jitter, int seed)
    {
        var rng = new Random(seed);
        var points = new List<(double, double)>(pointCount);
        for (var i = 0; i < pointCount; i++)
        {
            var t = (double)i / (pointCount - 1);
            var x = Math.Round((startX + t * length + (rng.NextDouble() - 0.5) * jitter) * 100) / 100;
            var y = Math.Round((startY + (rng.NextDouble() - 0.5) * jitter) * 100) / 100;
            points.Add((x, y));
        }
        return points;
    }

    /// <summary>
    /// Generates a perfectly straight diagonal line with densely-spaced points.
    /// The algorithm should collapse nearly all interior points since they're collinear.
    /// </summary>
    private static List<(double X, double Y)> GenerateDiagonalLine(
        double startX, double startY, double endX, double endY, int pointCount)
    {
        var points = new List<(double, double)>(pointCount);
        for (var i = 0; i < pointCount; i++)
        {
            var t = (double)i / (pointCount - 1);
            var x = Math.Round((startX + t * (endX - startX)) * 100) / 100;
            var y = Math.Round((startY + t * (endY - startY)) * 100) / 100;
            points.Add((x, y));
        }
        return points;
    }

    /// <summary>
    /// Generates a line that follows a straight diagonal but with small perpendicular
    /// wobbles that simulate hand tremor. The wobble amplitude and frequency are
    /// randomized per-point to mimic natural hand instability.
    /// </summary>
    private static List<(double X, double Y)> GenerateWobblyLine(
        double startX, double startY, double endX, double endY,
        int pointCount, double maxWobble, int seed)
    {
        var rng = new Random(seed);
        var dx = endX - startX;
        var dy = endY - startY;
        var length = Math.Sqrt(dx * dx + dy * dy);
        // Unit normal perpendicular to the line direction.
        var nx = -dy / length;
        var ny = dx / length;

        var points = new List<(double, double)>(pointCount);
        for (var i = 0; i < pointCount; i++)
        {
            var t = (double)i / (pointCount - 1);
            // Baseline position along the diagonal.
            var bx = startX + t * dx;
            var by = startY + t * dy;
            // Wobble: smooth-ish offset using a sine of random-phase plus per-point noise,
            // scaled so endpoints have zero wobble (hand is steadier at start/end of stroke).
            var envelope = Math.Sin(t * Math.PI); // 0 at endpoints, 1 at midpoint
            var wobble = envelope * maxWobble *
                         (0.6 * Math.Sin(t * 30 + rng.NextDouble() * Math.PI) +
                          0.4 * (rng.NextDouble() - 0.5));
            var x = Math.Round((bx + wobble * nx) * 100) / 100;
            var y = Math.Round((by + wobble * ny) * 100) / 100;
            points.Add((x, y));
        }
        return points;
    }

    /// <summary>
    /// Converts C# point list to a JS array literal.
    /// </summary>
    private static string PointsToJsArray(List<(double X, double Y)> points)
    {
        var sb = new StringBuilder("[");
        for (var i = 0; i < points.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"{{x:{points[i].X},y:{points[i].Y}}}");
        }
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>
    /// Calls visvalingamWhyatt in Jint and returns the simplified points.
    /// </summary>
    private static List<(double X, double Y)> Compress(
        Engine engine, List<(double X, double Y)> points, double minArea = 2, int minPoints = 3)
    {
        var jsArray = PointsToJsArray(points);
        var result = engine.Evaluate(
            $"visvalingamWhyatt({jsArray}, {minArea}, {minPoints})");

        var arr = result.AsArray();
        var simplified = new List<(double, double)>((int)arr.Length);
        for (uint i = 0; i < arr.Length; i++)
        {
            var obj = arr[i].AsObject();
            var x = obj.Get("x").AsNumber();
            var y = obj.Get("y").AsNumber();
            simplified.Add((x, y));
        }
        return simplified;
    }

    /// <summary>
    /// Calls buildPath in Jint and returns the SVG path d-attribute string.
    /// </summary>
    private static string BuildPath(Engine engine, List<(double X, double Y)> points)
    {
        var jsArray = PointsToJsArray(points);
        return engine.Evaluate($"buildPath({jsArray})").AsString();
    }

    /// <summary>
    /// Wraps path d-attributes in a complete SVG document.
    /// </summary>
    private static string WrapInSvg(
        List<string> pathDAttributes, int width = 800, int height = 600, string strokeColor = "#000000", int strokeWidth = 3)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"""<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" viewBox="0 0 {width} {height}">""");
        sb.AppendLine($"""  <rect width="{width}" height="{height}" fill="white"/>""");
        foreach (var d in pathDAttributes)
        {
            sb.AppendLine($"""  <path d="{d}" stroke="{strokeColor}" stroke-width="{strokeWidth}" fill="none" stroke-linecap="round" stroke-linejoin="round"/>""");
        }
        sb.AppendLine("</svg>");
        return sb.ToString();
    }

    // ----- Tests -----

    [TestMethod]
    public void TriangleArea_ReturnsCorrectArea()
    {
        var engine = CreateEngine();

        // Right triangle with legs 3 and 4: area = 12 (cross-product, not halved)
        var result = engine.Evaluate("triangleArea({x:0,y:0}, {x:3,y:0}, {x:0,y:4})").AsNumber();
        Assert.AreEqual(12.0, result, 0.001);

        // Collinear points: area = 0
        var collinear = engine.Evaluate("triangleArea({x:0,y:0}, {x:5,y:5}, {x:10,y:10})").AsNumber();
        Assert.AreEqual(0.0, collinear, 0.001);

        // Single point repeated: area = 0
        var degenerate = engine.Evaluate("triangleArea({x:1,y:1}, {x:1,y:1}, {x:1,y:1})").AsNumber();
        Assert.AreEqual(0.0, degenerate, 0.001);
    }

    [TestMethod]
    public void VisvalingamWhyatt_PreservesEndpoints()
    {
        var engine = CreateEngine();

        var points = GenerateSineWave(0, 100, 500, 400, 50, 5);
        var simplified = Compress(engine, points);

        Assert.IsTrue(simplified.Count >= 3, "Should retain at least minPoints (3)");
        Assert.AreEqual(points[0].X, simplified[0].X, 0.01, "First point X preserved");
        Assert.AreEqual(points[0].Y, simplified[0].Y, 0.01, "First point Y preserved");
        Assert.AreEqual(points[^1].X, simplified[^1].X, 0.01, "Last point X preserved");
        Assert.AreEqual(points[^1].Y, simplified[^1].Y, 0.01, "Last point Y preserved");
    }

    [TestMethod]
    public void VisvalingamWhyatt_ReducesPointCount()
    {
        var engine = CreateEngine();

        var points = GenerateSpiral(400, 300, 800, 200, 5);
        var simplified = Compress(engine, points);

        Assert.IsTrue(simplified.Count < points.Count,
            $"Expected fewer points after compression. Original: {points.Count}, Compressed: {simplified.Count}");
    }

    [TestMethod]
    public void VisvalingamWhyatt_ShortInput_ReturnsUnchanged()
    {
        var engine = CreateEngine();

        // 3 points with minPoints=3 should return all 3
        var points = new List<(double X, double Y)> { (0, 0), (50, 50), (100, 0) };
        var simplified = Compress(engine, points);

        Assert.AreEqual(3, simplified.Count);
    }

    [TestMethod]
    public void VisvalingamWhyatt_HigherMinArea_RemovesMorePoints()
    {
        var engine = CreateEngine();

        var points = GenerateSineWave(0, 200, 600, 500, 30, 8);
        var subtleSimplified = Compress(engine, points, minArea: 0.5);
        var aggressiveSimplified = Compress(engine, points, minArea: 8);

        Assert.IsTrue(aggressiveSimplified.Count < subtleSimplified.Count,
            $"Aggressive (minArea=8) should remove more points. Subtle: {subtleSimplified.Count}, Aggressive: {aggressiveSimplified.Count}");
    }

    [TestMethod]
    public void VisvalingamWhyatt_DiagonalLine_CollapsesCollinearPoints()
    {
        var engine = CreateEngine();

        // A perfect diagonal with 800 collinear points should collapse to just minPoints (3).
        var points = GenerateDiagonalLine(0, 0, 500, 500, 800);
        var simplified = Compress(engine, points);

        Assert.AreEqual(3, simplified.Count,
            $"A perfectly straight line should collapse to minPoints. Got {simplified.Count}");
    }

    [TestMethod]
    public void VisvalingamWhyatt_WobblyLine_RetainsMorePointsThanPerfectLine()
    {
        var engine = CreateEngine();

        var straight = GenerateDiagonalLine(0, 0, 500, 500, 800);
        var wobbly = GenerateWobblyLine(0, 0, 500, 500, 800, maxWobble: 4, seed: 42);

        var straightSimplified = Compress(engine, straight);
        var wobblySimplified = Compress(engine, wobbly);

        Assert.IsTrue(wobblySimplified.Count > straightSimplified.Count,
            $"Wobbly line ({wobblySimplified.Count} points) should retain more detail than " +
            $"a perfect line ({straightSimplified.Count} points)");
    }

    [TestMethod]
    public void VisvalingamWhyatt_WobblyLine_StillCompressesSignificantly()
    {
        var engine = CreateEngine();

        // Hand wobble of ~3px should mostly be removed since minArea=2 filters small deviations.
        var points = GenerateWobblyLine(50, 50, 700, 400, 1000, maxWobble: 3, seed: 55);
        var simplified = Compress(engine, points);

        var ratio = (double)simplified.Count / points.Count;
        Assert.IsTrue(ratio < 0.5,
            $"Subtle wobble (3px) should compress to under 50% of original. " +
            $"Got {simplified.Count}/{points.Count} = {ratio:P1}");
    }

    [TestMethod]
    public void BuildPath_SinglePoint_ReturnsMoveTo()
    {
        var engine = CreateEngine();

        var result = BuildPath(engine, [(42.5, 73.1)]);
        Assert.AreEqual("M 42.5 73.1", result);
    }

    [TestMethod]
    public void BuildPath_TwoPoints_ReturnsLineSegment()
    {
        var engine = CreateEngine();

        var result = BuildPath(engine, [(0, 0), (100, 200)]);
        Assert.IsTrue(result.StartsWith("M 0 0"), "Should start with MoveTo");
        Assert.IsTrue(result.Contains("L 100 200"), "Should end with LineTo for last point");
    }

    [TestMethod]
    public void ComplexSvg_CompressionReducesFileSize_And_OutputsForComparison()
    {
        var engine = CreateEngine();

        // Generate multiple complex paths to reach ~50kb total uncompressed SVG.
        var allPaths = new List<(string Name, List<(double X, double Y)> Points)>
        {
            ("Spiral-1", GenerateSpiral(200, 200, 1200, 180, 6)),
            ("Spiral-2", GenerateSpiral(600, 200, 1000, 150, 4)),
            ("SineWave-1", GenerateSineWave(50, 350, 1500, 700, 60, 10)),
            ("SineWave-2", GenerateSineWave(50, 450, 1200, 700, 40, 7)),
            ("SineWave-3", GenerateSineWave(50, 550, 800, 700, 25, 15)),
            ("Jagged-1", GenerateJaggedPath(50, 150, 1000, 700, 20, seed: 42)),
            ("Jagged-2", GenerateJaggedPath(50, 250, 1000, 700, 15, seed: 99)),
            ("Jagged-3", GenerateJaggedPath(50, 500, 800, 700, 30, seed: 7)),
            ("Diagonal-1", GenerateDiagonalLine(50, 50, 750, 550, 800)),
            ("Diagonal-2", GenerateDiagonalLine(750, 50, 50, 550, 600)),
            ("WobblyLine-1", GenerateWobblyLine(50, 100, 750, 500, 900, maxWobble: 3, seed: 11)),
            ("WobblyLine-2", GenerateWobblyLine(100, 550, 700, 80, 1000, maxWobble: 5, seed: 23)),
            ("WobblyLine-3", GenerateWobblyLine(400, 50, 400, 550, 700, maxWobble: 2, seed: 37)),
        };

        var originalDAttributes = new List<string>();
        var compressedDAttributes = new List<string>();
        var totalOriginalPoints = 0;
        var totalCompressedPoints = 0;

        foreach (var (name, points) in allPaths)
        {
            var originalD = BuildPath(engine, points);
            originalDAttributes.Add(originalD);

            var simplified = Compress(engine, points);
            var compressedD = BuildPath(engine, simplified);
            compressedDAttributes.Add(compressedD);

            totalOriginalPoints += points.Count;
            totalCompressedPoints += simplified.Count;

            Console.WriteLine($"  {name}: {points.Count} -> {simplified.Count} points " +
                              $"({100.0 * simplified.Count / points.Count:F1}%)");
        }

        var originalSvg = WrapInSvg(originalDAttributes);
        var compressedSvg = WrapInSvg(compressedDAttributes);

        var originalBytes = Encoding.UTF8.GetByteCount(originalSvg);
        var compressedBytes = Encoding.UTF8.GetByteCount(compressedSvg);

        Console.WriteLine($"\n  Total points: {totalOriginalPoints} -> {totalCompressedPoints} " +
                          $"({100.0 * totalCompressedPoints / totalOriginalPoints:F1}%)");
        Console.WriteLine($"  SVG size: {originalBytes:N0} bytes -> {compressedBytes:N0} bytes " +
                          $"({100.0 * compressedBytes / originalBytes:F1}%)");

        // Write output files for visual comparison.
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var outputDir = Path.Combine(repoRoot, "TestResults", "SvgCompression");
        Directory.CreateDirectory(outputDir);

        var originalPath = Path.Combine(outputDir, "original.svg");
        var compressedPath = Path.Combine(outputDir, "compressed.svg");
        File.WriteAllText(originalPath, originalSvg);
        File.WriteAllText(compressedPath, compressedSvg);

        Console.WriteLine($"\n  Output written to:");
        Console.WriteLine($"    {originalPath}");
        Console.WriteLine($"    {compressedPath}");

        // Assertions
        Assert.IsTrue(originalBytes >= 40_000,
            $"Original SVG should be ~50kb for a meaningful test, got {originalBytes:N0} bytes");
        Assert.IsTrue(compressedBytes < originalBytes,
            $"Compressed SVG ({compressedBytes:N0} bytes) should be smaller than original ({originalBytes:N0} bytes)");
        Assert.IsTrue(totalCompressedPoints < totalOriginalPoints,
            "Total compressed point count should be less than original");
        Assert.IsTrue(compressedSvg.Contains("<svg"), "Compressed output should be valid SVG");
        Assert.IsTrue(compressedSvg.Contains("<path"), "Compressed output should contain path elements");
    }

    [GeneratedRegex(@"export\s+function\s+")]
    private static partial Regex ExportFunctionRegex();

    [GeneratedRegex(@"export\s+const\s+")]
    private static partial Regex ExportConstRegex();
}
