using System.Text;

namespace KnockBox.CoreTests.Unit;

public partial class SvgCompressionTests
{
    // ----- Bézier fitting data types -----

    private readonly record struct BezierPoint(double X, double Y)
    {
        public static BezierPoint operator +(BezierPoint a, BezierPoint b) => new(a.X + b.X, a.Y + b.Y);
        public static BezierPoint operator -(BezierPoint a, BezierPoint b) => new(a.X - b.X, a.Y - b.Y);
        public static BezierPoint operator *(double s, BezierPoint p) => new(s * p.X, s * p.Y);
        public double LengthSq() => X * X + Y * Y;
        public double Length() => Math.Sqrt(LengthSq());
        public BezierPoint Normalize()
        {
            var l = Length();
            return l < 1e-12 ? new(0, 0) : new(X / l, Y / l);
        }
        public static BezierPoint operator -(BezierPoint p) => new(-p.X, -p.Y);
        public static double Dot(BezierPoint a, BezierPoint b) => a.X * b.X + a.Y * b.Y;
    }

    private readonly record struct CubicBezier(BezierPoint P0, BezierPoint P1, BezierPoint P2, BezierPoint P3);

    // ----- Schneider cubic Bézier fitting -----

    private static List<CubicBezier> FitCurve(
        List<(double X, double Y)> rawPoints, double tolerance, double cornerAngleDegrees = 70.0)
    {
        if (rawPoints.Count < 2)
            return [];

        var points = rawPoints.Select(p => new BezierPoint(p.X, p.Y)).ToArray();
        var result = new List<CubicBezier>();

        // Find corners to split the polyline into smooth sub-segments.
        var corners = new List<int> { 0 };
        var cosThreshold = Math.Cos(cornerAngleDegrees * Math.PI / 180.0);

        for (var i = 1; i < points.Length - 1; i++)
        {
            var v1 = (points[i] - points[i - 1]).Normalize();
            var v2 = (points[i + 1] - points[i]).Normalize();
            if (BezierPoint.Dot(v1, v2) < cosThreshold)
                corners.Add(i);
        }
        corners.Add(points.Length - 1);

        // Fit each sub-segment between corners.
        for (var c = 0; c < corners.Count - 1; c++)
        {
            var first = corners[c];
            var last = corners[c + 1];
            if (last - first < 1) continue;

            var tangent1 = (points[first + 1] - points[first]).Normalize();
            var tangent2 = (points[last - 1] - points[last]).Normalize();

            FitCubic(points, first, last, tangent1, tangent2, tolerance, result, 0);
        }

        return result;
    }

    private static void FitCubic(
        BezierPoint[] points, int first, int last,
        BezierPoint tangent1, BezierPoint tangent2,
        double tolerance, List<CubicBezier> result, int depth)
    {
        const int maxDepth = 30;
        var count = last - first + 1;

        // Base case: only 2 points — make a line-like cubic.
        if (count == 2)
        {
            var dist = (points[last] - points[first]).Length() / 3.0;
            result.Add(new CubicBezier(
                points[first],
                points[first] + dist * tangent1,
                points[last] + dist * tangent2,
                points[last]));
            return;
        }

        // Chord-length parameterization.
        var u = ChordLengthParameterize(points, first, last);

        // Fit and measure error.
        var bezier = GenerateBezier(points, first, last, u, tangent1, tangent2);
        var (maxError, splitIndex) = ComputeMaxError(points, first, last, bezier, u);

        if (maxError < tolerance)
        {
            result.Add(bezier);
            return;
        }

        // Try Newton-Raphson reparameterization if error is within 4x tolerance.
        if (maxError < tolerance * 4 && depth < maxDepth)
        {
            var uPrime = Reparameterize(points, first, last, u, bezier);
            bezier = GenerateBezier(points, first, last, uPrime, tangent1, tangent2);
            (maxError, splitIndex) = ComputeMaxError(points, first, last, bezier, uPrime);

            if (maxError < tolerance)
            {
                result.Add(bezier);
                return;
            }
        }

        if (depth >= maxDepth)
        {
            result.Add(bezier);
            return;
        }

        // Split at point of max error and recurse.
        var splitTangent = ComputeCenterTangent(points, first + splitIndex);
        FitCubic(points, first, first + splitIndex, tangent1, -splitTangent, tolerance, result, depth + 1);
        FitCubic(points, first + splitIndex, last, splitTangent, tangent2, tolerance, result, depth + 1);
    }

    private static double[] ChordLengthParameterize(BezierPoint[] points, int first, int last)
    {
        var count = last - first + 1;
        var u = new double[count];
        u[0] = 0;
        for (var i = 1; i < count; i++)
            u[i] = u[i - 1] + (points[first + i] - points[first + i - 1]).Length();

        var totalLen = u[count - 1];
        if (totalLen > 1e-12)
        {
            for (var i = 1; i < count; i++)
                u[i] /= totalLen;
        }
        u[count - 1] = 1.0; // Ensure exact endpoint.
        return u;
    }

    private static CubicBezier GenerateBezier(
        BezierPoint[] points, int first, int last,
        double[] u, BezierPoint tangent1, BezierPoint tangent2)
    {
        var p0 = points[first];
        var p3 = points[last];
        var count = last - first + 1;

        // Build the 2×2 least-squares system for alpha1, alpha2.
        double c00 = 0, c01 = 0, c11 = 0, x0 = 0, x1 = 0;

        for (var i = 0; i < count; i++)
        {
            var t = u[i];
            var b1 = B1(t);
            var b2 = B2(t);
            var a1 = b1 * tangent1;
            var a2 = b2 * tangent2;

            c00 += BezierPoint.Dot(a1, a1);
            c01 += BezierPoint.Dot(a1, a2);
            c11 += BezierPoint.Dot(a2, a2);

            var tmp = points[first + i]
                      - ((B0(t) + b1) * p0 + (b2 + B3(t)) * p3);
            x0 += BezierPoint.Dot(a1, tmp);
            x1 += BezierPoint.Dot(a2, tmp);
        }

        // Solve via Cramer's rule.
        var det = c00 * c11 - c01 * c01;
        double alpha1, alpha2;

        if (Math.Abs(det) < 1e-12)
        {
            var dist = (p3 - p0).Length() / 3.0;
            alpha1 = dist;
            alpha2 = dist;
        }
        else
        {
            alpha1 = (x0 * c11 - x1 * c01) / det;
            alpha2 = (c00 * x1 - c01 * x0) / det;
        }

        // If alphas are negative or zero, fall back to heuristic.
        var segLength = (p3 - p0).Length();
        var epsilon = 1e-6 * segLength;

        if (alpha1 < epsilon || alpha2 < epsilon)
        {
            alpha1 = segLength / 3.0;
            alpha2 = segLength / 3.0;
        }

        return new CubicBezier(
            p0,
            p0 + alpha1 * tangent1,
            p3 + alpha2 * tangent2,
            p3);
    }

    private static (double MaxError, int SplitIndex) ComputeMaxError(
        BezierPoint[] points, int first, int last, CubicBezier bezier, double[] u)
    {
        var maxError = 0.0;
        var splitIndex = (last - first + 1) / 2;
        var count = last - first + 1;

        for (var i = 1; i < count - 1; i++)
        {
            var p = EvalBezier(bezier, u[i]);
            var err = (p - points[first + i]).LengthSq();
            if (err > maxError)
            {
                maxError = err;
                splitIndex = i;
            }
        }

        return (maxError, splitIndex);
    }

    private static double[] Reparameterize(
        BezierPoint[] points, int first, int last, double[] u, CubicBezier bezier)
    {
        var count = last - first + 1;
        var uPrime = new double[count];
        uPrime[0] = 0;
        uPrime[count - 1] = 1;

        for (var i = 1; i < count - 1; i++)
            uPrime[i] = NewtonRaphsonRootFind(bezier, points[first + i], u[i]);

        return uPrime;
    }

    private static double NewtonRaphsonRootFind(CubicBezier bezier, BezierPoint point, double u)
    {
        var q = EvalBezier(bezier, u);
        var q1 = EvalBezierDerivative(bezier, u);
        var q2 = EvalBezierSecondDerivative(bezier, u);

        var diff = q - point;
        var numerator = BezierPoint.Dot(diff, q1);
        var denominator = BezierPoint.Dot(q1, q1) + BezierPoint.Dot(diff, q2);

        if (Math.Abs(denominator) < 1e-12)
            return u;

        var improved = u - numerator / denominator;
        return Math.Clamp(improved, 0.0, 1.0);
    }

    private static BezierPoint ComputeCenterTangent(BezierPoint[] points, int index)
    {
        if (index <= 0) return (points[1] - points[0]).Normalize();
        if (index >= points.Length - 1) return (points[^1] - points[^2]).Normalize();
        return (points[index + 1] - points[index - 1]).Normalize();
    }

    // ----- Bernstein basis and Bézier evaluation -----

    private static double B0(double t) { var s = 1 - t; return s * s * s; }
    private static double B1(double t) { var s = 1 - t; return 3 * t * s * s; }
    private static double B2(double t) { var s = 1 - t; return 3 * t * t * s; }
    private static double B3(double t) => t * t * t;

    private static BezierPoint EvalBezier(CubicBezier b, double t) =>
        B0(t) * b.P0 + B1(t) * b.P1 + B2(t) * b.P2 + B3(t) * b.P3;

    private static BezierPoint EvalBezierDerivative(CubicBezier b, double t)
    {
        var s = 1 - t;
        return 3 * s * s * (b.P1 - b.P0)
             + 6 * s * t * (b.P2 - b.P1)
             + 3 * t * t * (b.P3 - b.P2);
    }

    private static BezierPoint EvalBezierSecondDerivative(CubicBezier b, double t)
    {
        var s = 1 - t;
        return 6 * s * (b.P2 - 2 * b.P1 + b.P0)
             + 6 * t * (b.P3 - 2 * b.P2 + b.P1);
    }

    // ----- SVG path generation from Bézier curves -----

    private static string BezierCurvesToSvgPath(List<CubicBezier> curves)
    {
        if (curves.Count == 0) return "";

        var sb = new StringBuilder();
        var r = (double n) => Math.Round(n * 100) / 100;

        BezierPoint? prevEnd = null;
        foreach (var c in curves)
        {
            // Emit a new M if this curve doesn't continue from the previous endpoint.
            if (prevEnd is null ||
                Math.Abs(c.P0.X - prevEnd.Value.X) > 0.01 ||
                Math.Abs(c.P0.Y - prevEnd.Value.Y) > 0.01)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append($"M {r(c.P0.X)} {r(c.P0.Y)}");
            }

            sb.Append($" C {r(c.P1.X)} {r(c.P1.Y)} {r(c.P2.X)} {r(c.P2.Y)} {r(c.P3.X)} {r(c.P3.Y)}");
            prevEnd = c.P3;
        }

        return sb.ToString();
    }

    // ----- Comparison test -----

    [TestMethod]
    public void CompressionComparison_VW_vs_BezierFit_OutputsThreeSvgs()
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

        var originalDAttrs = new List<string>();
        var vwDAttrs = new List<string>();
        var bezierDAttrs = new List<string>();

        TestContext.WriteLine(string.Empty);
        TestContext.WriteLine("  Path             | Orig Pts  | VW Pts     (%)  | VW d-size  | Bezier Segs   (%)  | Bez d-size");
        TestContext.WriteLine("  -----------------+-----------+-----------------+------------+--------------------+-----------");

        foreach (var (name, points) in allPaths)
        {
            // Original
            var origD = BuildPath(engine, points);
            originalDAttrs.Add(origD);

            // VW compression
            var vwSimplified = Compress(engine, points);
            var vwD = BuildPath(engine, vwSimplified);
            vwDAttrs.Add(vwD);

            // Bézier fitting
            var bezierCurves = FitCurve(points, tolerance: 1.5);
            var bezD = BezierCurvesToSvgPath(bezierCurves);
            bezierDAttrs.Add(bezD);

            var vwDSize = Encoding.UTF8.GetByteCount(vwD);
            var bezDSize = Encoding.UTF8.GetByteCount(bezD);

            TestContext.WriteLine(
                $"  {name,-16} | {points.Count,9} | " +
                $"{vwSimplified.Count,9} {100.0 * vwSimplified.Count / points.Count,4:F0}% | " +
                $"{vwDSize,10:N0} | " +
                $"{bezierCurves.Count,12} {100.0 * bezierCurves.Count / points.Count,4:F0}% | " +
                $"{bezDSize,11:N0}");
        }

        var origSvg = WrapInSvg(originalDAttrs);
        var vwSvg = WrapInSvg(vwDAttrs);
        var bezSvg = WrapInSvg(bezierDAttrs);

        var origBytes = Encoding.UTF8.GetByteCount(origSvg);
        var vwBytes = Encoding.UTF8.GetByteCount(vwSvg);
        var bezBytes = Encoding.UTF8.GetByteCount(bezSvg);

        TestContext.WriteLine(string.Empty);
        TestContext.WriteLine($"  Total SVG sizes:");
        TestContext.WriteLine($"    Original:  {origBytes,10:N0} bytes");
        TestContext.WriteLine($"    VW:        {vwBytes,10:N0} bytes ({100.0 * vwBytes / origBytes:F1}%)");
        TestContext.WriteLine($"    Bézier:    {bezBytes,10:N0} bytes ({100.0 * bezBytes / origBytes:F1}%)");

        // Write output files for visual comparison.
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var outputDir = Path.Combine(repoRoot, "TestResults", "SvgCompression");
        Directory.CreateDirectory(outputDir);

        File.WriteAllText(Path.Combine(outputDir, "comparison_original.svg"), origSvg);
        File.WriteAllText(Path.Combine(outputDir, "comparison_vw.svg"), vwSvg);
        File.WriteAllText(Path.Combine(outputDir, "comparison_bezier.svg"), bezSvg);

        TestContext.WriteLine(string.Empty);
        TestContext.WriteLine($"  Output written to: {outputDir}");
        TestContext.WriteLine($"    comparison_original.svg");
        TestContext.WriteLine($"    comparison_vw.svg");
        TestContext.WriteLine($"    comparison_bezier.svg");

        // Assertions
        Assert.IsLessThan(origBytes, bezBytes,
            $"Bézier SVG ({bezBytes:N0} bytes) should be smaller than original ({origBytes:N0} bytes)");
        Assert.IsLessThan(origBytes, vwBytes,
            $"VW SVG ({vwBytes:N0} bytes) should be smaller than original ({origBytes:N0} bytes)");
        Assert.Contains("<path", bezSvg, "Bézier SVG should contain path elements");
        Assert.IsTrue(bezierDAttrs.TrueForAll(d => d.Contains('C')),
            "All Bézier path d-attributes should contain C (cubic) commands");
    }
}
