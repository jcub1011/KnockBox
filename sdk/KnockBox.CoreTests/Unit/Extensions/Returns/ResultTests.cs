using KnockBox.Core.Extensions.Returns;

namespace KnockBox.Tests.Unit.Extensions.Returns;

[TestClass]
public sealed class ResultErrorTests
{
    [TestMethod]
    public void ResultError_SingleMessageCtor_BothMessagesAreEqual()
    {
        var error = new ResultError("some error");

        Assert.AreEqual("some error", error.PublicMessage);
        Assert.AreEqual("some error", error.InternalMessage);
    }

    [TestMethod]
    public void ResultError_TwoMessageCtor_MessagesAreDistinct()
    {
        var error = new ResultError("public", "internal");

        Assert.AreEqual("public", error.PublicMessage);
        Assert.AreEqual("internal", error.InternalMessage);
    }
}

[TestClass]
public sealed class ResultTests
{
    [TestMethod]
    public void Success_IsSuccess_True()
    {
        var result = Result.Success;

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.IsCanceled);
        Assert.IsFalse(result.IsFailure);
    }

    [TestMethod]
    public void Canceled_IsCanceled_True()
    {
        var result = Result.Canceled;

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.IsCanceled);
        Assert.IsFalse(result.IsFailure);
    }

    [TestMethod]
    public void FromError_WithMessage_IsFailure()
    {
        var result = Result.FromError("oops");

        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(result.IsCanceled);
        Assert.IsTrue(result.IsFailure);
    }

    [TestMethod]
    public void FromError_WithResultError_IsFailure()
    {
        var result = Result.FromError(new ResultError("pub", "int"));

        Assert.IsTrue(result.IsFailure);
    }

    [TestMethod]
    public void TryGetFailure_OnSuccess_ReturnsFalse()
    {
        var result = Result.Success;

        Assert.IsFalse(result.TryGetFailure(out var error));
        Assert.AreEqual(default(ResultError), error);
    }

    [TestMethod]
    public void TryGetFailure_OnFailure_ReturnsTrueWithError()
    {
        var result = Result.FromError("something went wrong");

        Assert.IsTrue(result.TryGetFailure(out var error));
        Assert.AreEqual("something went wrong", error.PublicMessage);
    }

    [TestMethod]
    public void TryGetFailure_OnCanceled_ReturnsFalse()
    {
        var result = Result.Canceled;

        Assert.IsFalse(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void ImplicitConversion_FromResultError_IsFailure()
    {
        Result result = new ResultError("err");

        Assert.IsTrue(result.IsFailure);
        Assert.IsTrue(result.TryGetFailure(out var error));
        Assert.AreEqual("err", error.PublicMessage);
    }

    [TestMethod]
    public void FromError_TwoPartMessage_BothMessagesStored()
    {
        var result = Result.FromError("public msg", "internal msg");

        Assert.IsTrue(result.TryGetFailure(out var error));
        Assert.AreEqual("public msg", error.PublicMessage);
        Assert.AreEqual("internal msg", error.InternalMessage);
    }
}

[TestClass]
public sealed class ResultTErrorTests
{
    [TestMethod]
    public void Success_IsSuccess_True()
    {
        var result = Result<string>.Success;

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.IsCanceled);
        Assert.IsFalse(result.IsFailure);
    }

    [TestMethod]
    public void Canceled_IsCanceled_True()
    {
        var result = Result<string>.Canceled;

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.IsCanceled);
        Assert.IsFalse(result.IsFailure);
    }

    [TestMethod]
    public void FromError_IsFailure()
    {
        var result = Result<string>.FromError("error msg");

        Assert.IsTrue(result.IsFailure);
    }

    [TestMethod]
    public void TryGetFailure_OnSuccess_ReturnsFalse()
    {
        var result = Result<string>.Success;

        Assert.IsFalse(result.TryGetFailure(out _));
    }

    [TestMethod]
    public void TryGetFailure_OnFailure_ReturnsTrueWithError()
    {
        var result = Result<string>.FromError("err");

        Assert.IsTrue(result.TryGetFailure(out var error));
        Assert.AreEqual("err", error);
    }

    [TestMethod]
    public void TryGetFailure_OnCanceled_ReturnsFalse()
    {
        Assert.IsFalse(Result<string>.Canceled.TryGetFailure(out _));
    }

    [TestMethod]
    public void ImplicitConversion_FromError_IsFailure()
    {
        Result<int> result = 42;

        Assert.IsTrue(result.IsFailure);
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual(42, err);
    }
}

[TestClass]
public sealed class ValueResultTests
{
    [TestMethod]
    public void FromValue_IsSuccess()
    {
        var result = ValueResult<int>.FromValue(99);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.IsCanceled);
        Assert.IsFalse(result.IsFailure);
    }

    [TestMethod]
    public void TryGetSuccess_OnSuccess_ReturnsTrueWithValue()
    {
        var result = ValueResult<int>.FromValue(7);

        Assert.IsTrue(result.TryGetSuccess(out var value));
        Assert.AreEqual(7, value);
    }

    [TestMethod]
    public void TryGetSuccess_OnFailure_ReturnsFalse()
    {
        var result = ValueResult<int>.FromError("err");

        Assert.IsFalse(result.TryGetSuccess(out _));
    }

    [TestMethod]
    public void TryGetSuccess_OnCanceled_ReturnsFalse()
    {
        Assert.IsFalse(ValueResult<int>.Canceled.TryGetSuccess(out _));
    }

    [TestMethod]
    public void FromError_IsFailure()
    {
        var result = ValueResult<int>.FromError("bad");

        Assert.IsTrue(result.IsFailure);
        Assert.IsTrue(result.TryGetFailure(out var error));
        Assert.AreEqual("bad", error.PublicMessage);
    }

    [TestMethod]
    public void TryGetFailure_OnSuccess_ReturnsFalse()
    {
        Assert.IsFalse(ValueResult<int>.FromValue(1).TryGetFailure(out _));
    }

    [TestMethod]
    public void TryGetFailure_OnCanceled_ReturnsFalse()
    {
        Assert.IsFalse(ValueResult<int>.Canceled.TryGetFailure(out _));
    }

    [TestMethod]
    public void Canceled_IsCanceled_True()
    {
        var result = ValueResult<int>.Canceled;

        Assert.IsTrue(result.IsCanceled);
        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(result.IsFailure);
    }

    [TestMethod]
    public void ImplicitConversion_FromValue_IsSuccess()
    {
        ValueResult<string> result = "hello";

        Assert.IsTrue(result.IsSuccess);
        Assert.IsTrue(result.TryGetSuccess(out var val));
        Assert.AreEqual("hello", val);
    }

    [TestMethod]
    public void ImplicitConversion_FromResultError_IsFailure()
    {
        ValueResult<string> result = new ResultError("oops");

        Assert.IsTrue(result.IsFailure);
    }

    [TestMethod]
    public void FromError_TwoPart_BothMessagesStored()
    {
        var result = ValueResult<string>.FromError("pub", "int");

        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual("pub", err.PublicMessage);
        Assert.AreEqual("int", err.InternalMessage);
    }
}

[TestClass]
public sealed class ValueResultTErrorTests
{
    [TestMethod]
    public void FromValue_IsSuccess()
    {
        var result = ValueResult<int, string>.FromValue(5);

        Assert.IsTrue(result.IsSuccess);
        Assert.IsFalse(result.IsCanceled);
        Assert.IsFalse(result.IsFailure);
        Assert.IsTrue(result.TryGetSuccess(out var v));
        Assert.AreEqual(5, v);
    }

    [TestMethod]
    public void FromError_IsFailure()
    {
        var result = ValueResult<int, string>.FromError("fail");

        Assert.IsTrue(result.IsFailure);
        Assert.IsTrue(result.TryGetFailure(out var err));
        Assert.AreEqual("fail", err);
    }

    [TestMethod]
    public void Canceled_IsCanceled_True()
    {
        var result = ValueResult<int, string>.Canceled;

        Assert.IsTrue(result.IsCanceled);
        Assert.IsFalse(result.IsSuccess);
        Assert.IsFalse(result.IsFailure);
    }

    [TestMethod]
    public void TryGetSuccess_OnFailure_ReturnsFalse()
    {
        var result = ValueResult<int, string>.FromError("e");

        Assert.IsFalse(result.TryGetSuccess(out _));
    }

    [TestMethod]
    public void TryGetFailure_OnSuccess_ReturnsFalse()
    {
        Assert.IsFalse(ValueResult<int, string>.FromValue(1).TryGetFailure(out _));
    }

    [TestMethod]
    public void TryGetFailure_OnCanceled_ReturnsFalse()
    {
        Assert.IsFalse(ValueResult<int, string>.Canceled.TryGetFailure(out _));
    }

    [TestMethod]
    public void ImplicitConversion_FromValue_IsSuccess()
    {
        ValueResult<int, string> result = 42;

        Assert.IsTrue(result.IsSuccess);
    }

    [TestMethod]
    public void ImplicitConversion_FromError_IsFailure()
    {
        ValueResult<int, string> result = "boom";

        Assert.IsTrue(result.IsFailure);
    }
}

[TestClass]
public sealed class ErrorWrapperTests
{
    [TestMethod]
    public void FromError_TryGetError_ReturnsTrueWithError()
    {
        var wrapper = ErrorWrapper<string>.FromError("err");

        Assert.IsFalse(wrapper.IsCancellationError);
        Assert.IsTrue(wrapper.TryGetError(out var e));
        Assert.AreEqual("err", e);
    }

    [TestMethod]
    public void FromCancellation_TryGetError_ReturnsFalse()
    {
        var wrapper = ErrorWrapper<string>.FromCancellation();

        Assert.IsTrue(wrapper.IsCancellationError);
        Assert.IsFalse(wrapper.TryGetError(out _));
    }

    [TestMethod]
    public void ImplicitConversion_FromError_IsNotCancellation()
    {
        ErrorWrapper<int> wrapper = 42;

        Assert.IsFalse(wrapper.IsCancellationError);
        Assert.AreEqual(42, wrapper.Error);
    }

    [TestMethod]
    public void ImplicitConversion_FromOperationCanceledException_IsCancellation()
    {
        ErrorWrapper<int> wrapper = new OperationCanceledException();

        Assert.IsTrue(wrapper.IsCancellationError);
    }
}
