using KnockBox.Tooling.Math;

namespace KnockBox.Tests.Unit.Extensions.Math;

[TestClass]
public sealed class MathExtensionsTests
{
    // ── int.Pow ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void IntPow_AnyBase_ExponentZero_ReturnsOne()
    {
        Assert.AreEqual(1, 5.Pow(0));
        Assert.AreEqual(1, 0.Pow(0));
        Assert.AreEqual(1, (-3).Pow(0));
    }

    [TestMethod]
    public void IntPow_AnyBase_ExponentOne_ReturnsSelf()
    {
        Assert.AreEqual(7, 7.Pow(1));
        Assert.AreEqual(-4, (-4).Pow(1));
    }

    [TestMethod]
    public void IntPow_Two_PowersOfTwo_ReturnsCorrectValues()
    {
        Assert.AreEqual(1, 2.Pow(0));
        Assert.AreEqual(2, 2.Pow(1));
        Assert.AreEqual(4, 2.Pow(2));
        Assert.AreEqual(8, 2.Pow(3));
        Assert.AreEqual(1024, 2.Pow(10));
    }

    [TestMethod]
    public void IntPow_Ten_PowersOfTen_ReturnsCorrectValues()
    {
        Assert.AreEqual(1, 10.Pow(0));
        Assert.AreEqual(10, 10.Pow(1));
        Assert.AreEqual(100, 10.Pow(2));
        Assert.AreEqual(1000, 10.Pow(3));
    }

    [TestMethod]
    public void IntPow_NegativeBase_OddExponent_ReturnsNegative()
    {
        Assert.AreEqual(-8, (-2).Pow(3));
    }

    [TestMethod]
    public void IntPow_NegativeBase_EvenExponent_ReturnsPositive()
    {
        Assert.AreEqual(4, (-2).Pow(2));
        Assert.AreEqual(16, (-2).Pow(4));
    }

    [TestMethod]
    public void IntPow_Overflow_ThrowsOverflowException()
    {
        Assert.Throws<OverflowException>(() => int.MaxValue.Pow(2));
    }

    // ── long.Pow ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void LongPow_AnyBase_ExponentZero_ReturnsOne()
    {
        Assert.AreEqual(1L, 5L.Pow(0));
        Assert.AreEqual(1L, 0L.Pow(0));
    }

    [TestMethod]
    public void LongPow_AnyBase_ExponentOne_ReturnsSelf()
    {
        Assert.AreEqual(7L, 7L.Pow(1));
        Assert.AreEqual(-4L, (-4L).Pow(1));
    }

    [TestMethod]
    public void LongPow_Two_PowersOfTwo_ReturnsCorrectValues()
    {
        Assert.AreEqual(1L, 2L.Pow(0));
        Assert.AreEqual(2L, 2L.Pow(1));
        Assert.AreEqual(4L, 2L.Pow(2));
        Assert.AreEqual(1024L, 2L.Pow(10));
        Assert.AreEqual(1073741824L, 2L.Pow(30));
    }

    [TestMethod]
    public void LongPow_Overflow_ThrowsOverflowException()
    {
        Assert.Throws<OverflowException>(() => long.MaxValue.Pow(2));
    }
}
