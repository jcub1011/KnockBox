using KnockBox.Services.Drawing;

namespace KnockBox.Tests.Unit.Drawing;

[TestClass]
public sealed class SvgDrawingDocumentTests
{
    [TestMethod]
    public void BeginStroke_AppendPoint_EndStroke_AddsStroke()
    {
        var document = new SvgDrawingDocument();

        document.BeginStroke(10, 15, "#112233", 6);
        document.AppendPoint(18, 25);
        document.EndStroke();

        Assert.AreEqual(1, document.Strokes.Count);
        Assert.AreEqual("#112233", document.Strokes[0].Color);
        Assert.AreEqual(6d, document.Strokes[0].StrokeSize);
        CollectionAssert.AreEqual(
            new[]
            {
                new SvgDrawingPoint(10, 15),
                new SvgDrawingPoint(18, 25)
            },
            document.Strokes[0].Points.ToArray());
    }

    [TestMethod]
    public void Undo_RemovesLatestStroke()
    {
        var document = new SvgDrawingDocument();
        document.BeginStroke(5, 5, "#000000", 4);
        document.EndStroke();
        document.BeginStroke(10, 10, "#FFFFFF", 8);
        document.EndStroke();

        var result = document.Undo();

        Assert.IsTrue(result);
        Assert.AreEqual(1, document.Strokes.Count);
        Assert.AreEqual("#000000", document.Strokes[0].Color);
    }

    [TestMethod]
    public void ExportSvg_UsesBackgroundAndVectorElements()
    {
        var document = new SvgDrawingDocument();
        document.BeginStroke(10, 20, "#123456", 12);
        document.AppendPoint(20, 30);
        document.EndStroke();
        document.BeginStroke(40, 50, "#ABCDEF", 8);
        document.EndStroke();

        var svg = document.ExportSvg(200, 100, "#FEDCBA");

        StringAssert.Contains(svg, "<svg");
        StringAssert.Contains(svg, "width=\"200\"");
        StringAssert.Contains(svg, "height=\"100\"");
        StringAssert.Contains(svg, "viewBox=\"0 0 200 100\"");
        StringAssert.Contains(svg, "<rect width=\"100%\" height=\"100%\" fill=\"#FEDCBA\" />");
        StringAssert.Contains(svg, "<polyline points=\"10,20 20,30\" fill=\"none\" stroke=\"#123456\" stroke-width=\"12\"");
        StringAssert.Contains(svg, "<circle cx=\"40\" cy=\"50\" r=\"4\" fill=\"#ABCDEF\" />");
    }

    [TestMethod]
    public void NormalizeColor_InvalidValue_FallsBackToSafeColor()
    {
        var normalized = SvgDrawingDocument.NormalizeColor("\" onload=\"alert(1)", "#123456");

        Assert.AreEqual("#123456", normalized);
    }

    [TestMethod]
    public void NormalizeStrokeSize_InvalidOrOutOfRange_ClampsToSupportedRange()
    {
        Assert.AreEqual(SvgDrawingDocument.MinimumBrushSize, SvgDrawingDocument.NormalizeStrokeSize(double.NaN));
        Assert.AreEqual(SvgDrawingDocument.MinimumBrushSize, SvgDrawingDocument.NormalizeStrokeSize(0));
        Assert.AreEqual(SvgDrawingDocument.MaximumBrushSize, SvgDrawingDocument.NormalizeStrokeSize(1000));
    }
}
