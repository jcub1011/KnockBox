using KnockBox.Core.Primitives.Exceptions;

namespace KnockBox.Tests.Unit.Extensions.Exceptions;

[TestClass]
public sealed class ExceptionExtensionsTests
{
    // ── TryGetCancellationException ──────────────────────────────────────────

    [TestMethod]
    public void TryGetCancellationException_DirectOce_ReturnsTrueWithOce()
    {
        var oce = new OperationCanceledException("canceled");

        var found = oce.TryGetCancellationException(out var result);

        Assert.IsTrue(found);
        Assert.AreSame(oce, result);
    }

    [TestMethod]
    public void TryGetCancellationException_NonCancelException_ReturnsFalse()
    {
        var ex = new InvalidOperationException("not canceled");

        var found = ex.TryGetCancellationException(out var result);

        Assert.IsFalse(found);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void TryGetCancellationException_OceNestedInAggregate_ReturnsTrueWithOce()
    {
        var oce = new OperationCanceledException("inner cancel");
        var aggregate = new AggregateException("outer", oce);

        var found = aggregate.TryGetCancellationException(out var result);

        Assert.IsTrue(found);
        Assert.AreSame(oce, result);
    }

    [TestMethod]
    public void TryGetCancellationException_NoOceInAggregate_ReturnsFalse()
    {
        var aggregate = new AggregateException(new InvalidOperationException("nope"));

        var found = aggregate.TryGetCancellationException(out var result);

        Assert.IsFalse(found);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void TryGetCancellationException_OceAsInnerException_ReturnsTrueWithOce()
    {
        var oce = new OperationCanceledException("deep");
        var wrapper = new Exception("outer", oce);

        var found = wrapper.TryGetCancellationException(out var result);

        Assert.IsTrue(found);
        Assert.AreSame(oce, result);
    }

    // ── GetCancellationException on IEnumerable ──────────────────────────────

    [TestMethod]
    public void GetCancellationException_Enumerable_FindsFirstOce()
    {
        var oce = new OperationCanceledException("found");
        var exceptions = new Exception[]
        {
            new InvalidOperationException("nope"),
            oce,
            new ArgumentException("also nope"),
        };

        var result = exceptions.GetCancellationException();

        Assert.AreSame(oce, result);
    }

    [TestMethod]
    public void GetCancellationException_Enumerable_NonePresent_ReturnsNull()
    {
        var exceptions = new Exception[]
        {
            new InvalidOperationException("a"),
            new ArgumentException("b"),
        };

        var result = exceptions.GetCancellationException();

        Assert.IsNull(result);
    }

    // ── TryGetFormattedExceptionMessage ──────────────────────────────────────

    [TestMethod]
    public void TryGetFormattedExceptionMessage_FormattedException_ReturnsTrueWithPublicMessage()
    {
        var ex = new FormattedException("public message", "internal message");

        var found = ex.TryGetFormattedExceptionMessage(out var msg);

        Assert.IsTrue(found);
        Assert.AreEqual("public message", msg);
    }

    [TestMethod]
    public void TryGetFormattedExceptionMessage_PlainException_ReturnsFalseWithMessage()
    {
        var ex = new InvalidOperationException("plain message");

        var found = ex.TryGetFormattedExceptionMessage(out var msg);

        Assert.IsFalse(found);
        Assert.AreEqual("plain message", msg);
    }
}
