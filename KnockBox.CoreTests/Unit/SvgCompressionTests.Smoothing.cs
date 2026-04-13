using System.Text;

namespace KnockBox.CoreTests.Unit;

public partial class SvgCompressionTests
{
    // ----- Smoothing algorithms -----

    /// <summary>
    /// Moving average smoothing. Each point is replaced by the average of its neighbors
    /// within the given window. Endpoints are preserved exactly.
    /// </summary>
    private static List<(double X, double Y)> SmoothMovingAverage(
        List<(double X, double Y)> points, int windowSize)
    {
        if (points.Count <= 2 || windowSize <= 1) return new(points);

        var half = windowSize / 2;
        var result = new List<(double, double)>(points.Count);

        for (var i = 0; i < points.Count; i++)
        {
            // Preserve first and last points exactly.
            if (i == 0 || i == points.Count - 1)
            {
                result.Add(points[i]);
                continue;
            }

            var lo = Math.Max(0, i - half);
            var hi = Math.Min(points.Count - 1, i + half);
            var count = hi - lo + 1;
            double sx = 0, sy = 0;
            for (var j = lo; j <= hi; j++)
            {
                sx += points[j].X;
                sy += points[j].Y;
            }
            result.Add((Math.Round(sx / count * 100) / 100, Math.Round(sy / count * 100) / 100));
        }

        return result;
    }

    /// <summary>
    /// Gaussian-weighted smoothing. Each point is replaced by a weighted average of its
    /// neighbors, where the weights follow a Gaussian bell curve. This produces smoother
    /// results than a uniform moving average because nearby points contribute more than
    /// distant ones. Endpoints are preserved exactly.
    /// </summary>
    private static List<(double X, double Y)> SmoothGaussian(
        List<(double X, double Y)> points, int windowSize, double sigma)
    {
        if (points.Count <= 2 || windowSize <= 1) return new(points);

        var half = windowSize / 2;

        // Precompute Gaussian kernel weights.
        var kernel = new double[windowSize];
        for (var k = 0; k < windowSize; k++)
        {
            var d = k - half;
            kernel[k] = Math.Exp(-0.5 * d * d / (sigma * sigma));
        }

        var result = new List<(double, double)>(points.Count);

        for (var i = 0; i < points.Count; i++)
        {
            if (i == 0 || i == points.Count - 1)
            {
                result.Add(points[i]);
                continue;
            }

            double sx = 0, sy = 0, wSum = 0;
            for (var k = 0; k < windowSize; k++)
            {
                var j = i + k - half;
                if (j < 0 || j >= points.Count) continue;
                var w = kernel[k];
                sx += points[j].X * w;
                sy += points[j].Y * w;
                wSum += w;
            }
            result.Add((Math.Round(sx / wSum * 100) / 100, Math.Round(sy / wSum * 100) / 100));
        }

        return result;
    }

    /// <summary>
    /// Exponential moving average (EMA) smoothing, similar to Procreate's "streamline".
    /// Each point is blended with the running smoothed position:
    ///   smoothed = alpha * raw + (1 - alpha) * previous_smoothed
    /// Lower alpha = more smoothing. Endpoints are preserved exactly.
    /// Applied in both forward and reverse passes to avoid directional bias.
    /// </summary>
    private static List<(double X, double Y)> SmoothExponential(
        List<(double X, double Y)> points, double alpha)
    {
        if (points.Count <= 2 || alpha >= 1.0) return new(points);

        var count = points.Count;

        // Forward pass.
        var forward = new (double X, double Y)[count];
        forward[0] = points[0];
        for (var i = 1; i < count; i++)
        {
            forward[i] = (
                alpha * points[i].X + (1 - alpha) * forward[i - 1].X,
                alpha * points[i].Y + (1 - alpha) * forward[i - 1].Y);
        }

        // Reverse pass.
        var reverse = new (double X, double Y)[count];
        reverse[count - 1] = points[count - 1];
        for (var i = count - 2; i >= 0; i--)
        {
            reverse[i] = (
                alpha * points[i].X + (1 - alpha) * reverse[i + 1].X,
                alpha * points[i].Y + (1 - alpha) * reverse[i + 1].Y);
        }

        // Average of forward and reverse, preserving endpoints.
        var result = new List<(double, double)>(count);
        result.Add(points[0]);
        for (var i = 1; i < count - 1; i++)
        {
            result.Add((
                Math.Round((forward[i].X + reverse[i].X) / 2 * 100) / 100,
                Math.Round((forward[i].Y + reverse[i].Y) / 2 * 100) / 100));
        }
        result.Add(points[count - 1]);

        return result;
    }

    // ----- Smoothing comparison tests -----

    [TestMethod]
    public void SmoothingComparison_AllPipelines_OutputsSvgs()
    {
        var engine = CreateEngine();

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

        // Each pipeline: name, smooth function, then VW or Bézier fit.
        // We test smoothing as a pre-processing step before both VW and Bézier fitting.
        var pipelines = new List<(string Label, Func<List<(double X, double Y)>, string> Process)>
        {
            ("Original", pts => BuildPath(engine, pts)),

            ("VW only", pts =>
            {
                var simplified = Compress(engine, pts);
                return BuildPath(engine, simplified);
            }),

            ("Bezier only", pts =>
            {
                var curves = FitCurve(pts, tolerance: 1.5);
                return BezierCurvesToSvgPath(curves);
            }),

            ("MA(5) + VW", pts =>
            {
                var smoothed = SmoothMovingAverage(pts, 5);
                var simplified = Compress(engine, smoothed);
                return BuildPath(engine, simplified);
            }),

            ("Gauss(7,2) + VW", pts =>
            {
                var smoothed = SmoothGaussian(pts, 7, 2.0);
                var simplified = Compress(engine, smoothed);
                return BuildPath(engine, simplified);
            }),

            ("EMA(0.3) + VW", pts =>
            {
                var smoothed = SmoothExponential(pts, 0.3);
                var simplified = Compress(engine, smoothed);
                return BuildPath(engine, simplified);
            }),

            ("MA(5) + Bezier", pts =>
            {
                var smoothed = SmoothMovingAverage(pts, 5);
                var curves = FitCurve(smoothed, tolerance: 1.5);
                return BezierCurvesToSvgPath(curves);
            }),

            ("Gauss(7,2) + Bezier", pts =>
            {
                var smoothed = SmoothGaussian(pts, 7, 2.0);
                var curves = FitCurve(smoothed, tolerance: 1.5);
                return BezierCurvesToSvgPath(curves);
            }),

            ("EMA(0.3) + Bezier", pts =>
            {
                var smoothed = SmoothExponential(pts, 0.3);
                var curves = FitCurve(smoothed, tolerance: 1.5);
                return BezierCurvesToSvgPath(curves);
            }),
        };

        // Collect d-attributes per pipeline.
        var pipelineDAttrs = pipelines.Select(_ => new List<string>()).ToList();

        // Print per-path d-attribute sizes.
        Console.WriteLine();
        Console.WriteLine("  d-attribute byte sizes per path and pipeline:");
        Console.WriteLine();

        // Header
        var header = $"  {"Path",-16}";
        foreach (var (label, _) in pipelines)
            header += $" | {label,18}";
        Console.WriteLine(header);
        Console.WriteLine("  " + new string('-', header.Length - 2));

        foreach (var (name, points) in allPaths)
        {
            var line = $"  {name,-16}";
            for (var p = 0; p < pipelines.Count; p++)
            {
                var d = pipelines[p].Process(points);
                pipelineDAttrs[p].Add(d);
                var size = Encoding.UTF8.GetByteCount(d);
                line += $" | {size,18:N0}";
            }
            Console.WriteLine(line);
        }

        // Build SVGs and print totals.
        Console.WriteLine();
        Console.WriteLine("  Total SVG sizes:");

        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var outputDir = Path.Combine(repoRoot, "TestResults", "SvgCompression", "Smoothing");
        Directory.CreateDirectory(outputDir);

        var originalBytes = 0;

        for (var p = 0; p < pipelines.Count; p++)
        {
            var svg = WrapInSvg(pipelineDAttrs[p]);
            var bytes = Encoding.UTF8.GetByteCount(svg);

            if (p == 0) originalBytes = bytes;

            var pct = originalBytes > 0 ? $" ({100.0 * bytes / originalBytes:F1}%)" : "";
            Console.WriteLine($"    {pipelines[p].Label,-22} {bytes,10:N0} bytes{pct}");

            var fileName = pipelines[p].Label
                .Replace(" ", "_").Replace("(", "").Replace(")", "").Replace("+", "then").Replace(",", "_")
                .ToLowerInvariant();
            File.WriteAllText(Path.Combine(outputDir, $"{fileName}.svg"), svg);
        }

        Console.WriteLine();
        Console.WriteLine($"  Output written to: {outputDir}");

        // Assertions: every pipeline with smoothing should produce smaller SVG than original.
        for (var p = 1; p < pipelines.Count; p++)
        {
            var svg = WrapInSvg(pipelineDAttrs[p]);
            var bytes = Encoding.UTF8.GetByteCount(svg);
            Assert.IsTrue(bytes < originalBytes,
                $"Pipeline '{pipelines[p].Label}' ({bytes:N0} bytes) should be smaller than original ({originalBytes:N0} bytes)");
        }
    }

    [TestMethod]
    public void SmoothingEffect_WobblyLinesOnly_ShowsVisualDifference()
    {
        var engine = CreateEngine();

        // Focus on wobbly lines with varying wobble intensity to clearly show smoothing effects.
        var wobblyPaths = new List<(string Name, List<(double X, double Y)> Points)>
        {
            ("Wobble-2px", GenerateWobblyLine(50, 80, 750, 500, 900, maxWobble: 2, seed: 11)),
            ("Wobble-3px", GenerateWobblyLine(50, 180, 750, 500, 900, maxWobble: 3, seed: 22)),
            ("Wobble-5px", GenerateWobblyLine(50, 280, 700, 480, 1000, maxWobble: 5, seed: 33)),
            ("Wobble-8px", GenerateWobblyLine(50, 380, 700, 520, 800, maxWobble: 8, seed: 44)),
            ("Wobble-12px", GenerateWobblyLine(50, 480, 700, 550, 700, maxWobble: 12, seed: 55)),
        };

        // Each variant generates a separate SVG showing the same wobbly lines processed differently.
        var variants = new List<(string Label, Func<List<(double X, double Y)>, string> Process)>
        {
            ("original", pts => BuildPath(engine, pts)),

            ("vw_only", pts =>
            {
                var simplified = Compress(engine, pts);
                return BuildPath(engine, simplified);
            }),

            ("ma5_then_vw", pts =>
            {
                var smoothed = SmoothMovingAverage(pts, 5);
                var simplified = Compress(engine, smoothed);
                return BuildPath(engine, simplified);
            }),

            ("gauss7_then_vw", pts =>
            {
                var smoothed = SmoothGaussian(pts, 7, 2.0);
                var simplified = Compress(engine, smoothed);
                return BuildPath(engine, simplified);
            }),

            ("ema03_then_vw", pts =>
            {
                var smoothed = SmoothExponential(pts, 0.3);
                var simplified = Compress(engine, smoothed);
                return BuildPath(engine, simplified);
            }),

            ("ema02_then_vw", pts =>
            {
                var smoothed = SmoothExponential(pts, 0.2);
                var simplified = Compress(engine, smoothed);
                return BuildPath(engine, simplified);
            }),

            ("ema03_then_bezier", pts =>
            {
                var smoothed = SmoothExponential(pts, 0.3);
                var curves = FitCurve(smoothed, tolerance: 1.5);
                return BezierCurvesToSvgPath(curves);
            }),
        };

        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var outputDir = Path.Combine(repoRoot, "TestResults", "SvgCompression", "WobblySmoothing");
        Directory.CreateDirectory(outputDir);

        Console.WriteLine();
        Console.WriteLine("  Wobbly line smoothing comparison (d-attribute bytes):");
        Console.WriteLine();

        // Header
        var header = $"  {"Path",-14}";
        foreach (var (label, _) in variants)
            header += $" | {label,17}";
        Console.WriteLine(header);
        Console.WriteLine("  " + new string('-', header.Length - 2));

        foreach (var variant in variants)
        {
            var dAttrs = new List<string>();
            var line = new StringBuilder();

            foreach (var (name, points) in wobblyPaths)
            {
                var d = variant.Process(points);
                dAttrs.Add(d);
            }

            // Write SVG for this variant.
            var svg = WrapInSvg(dAttrs);
            File.WriteAllText(Path.Combine(outputDir, $"wobbly_{variant.Label}.svg"), svg);
        }

        // Print table: rows are paths, columns are variants.
        foreach (var (name, points) in wobblyPaths)
        {
            var line = $"  {name,-14}";
            foreach (var (_, process) in variants)
            {
                var d = process(points);
                line += $" | {Encoding.UTF8.GetByteCount(d),17:N0}";
            }
            Console.WriteLine(line);
        }

        Console.WriteLine();
        Console.WriteLine("  SVG totals:");

        var origSvg = WrapInSvg([.. wobblyPaths.Select(p => BuildPath(engine, p.Points))]);
        var origSize = Encoding.UTF8.GetByteCount(origSvg);

        foreach (var (label, process) in variants)
        {
            var dAttrs = wobblyPaths.Select(p => process(p.Points)).ToList();
            var svg = WrapInSvg(dAttrs);
            var size = Encoding.UTF8.GetByteCount(svg);
            Console.WriteLine($"    {label,-22} {size,8:N0} bytes ({100.0 * size / origSize:F1}%)");
        }

        Console.WriteLine();
        Console.WriteLine($"  Output written to: {outputDir}");

        // Assertions
        var emaVwDAttrs = wobblyPaths.Select(p =>
        {
            var smoothed = SmoothExponential(p.Points, 0.3);
            var simplified = Compress(engine, smoothed);
            return BuildPath(engine, simplified);
        }).ToList();
        var emaVwSvg = WrapInSvg(emaVwDAttrs);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(emaVwSvg) < origSize,
            "EMA + VW smoothed output should be smaller than unprocessed original");
    }
}
