using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Core.Primitives.Returns
{
    /// <summary>
    /// Discriminator for <see cref="ErrorWrapper{TError}"/>.
    /// </summary>
    internal enum ErrorKind : byte
    {
        Failure = 0,
        Cancellation = 1,
    }

    /// <summary>
    /// A wrapper used to discern between failures and cancellations.
    /// </summary>
    /// <typeparam name="TError"></typeparam>
    public readonly struct ErrorWrapper<TError>
    {
        private readonly ErrorKind _kind;
        public readonly TError Error;

        public bool IsCancellationError => _kind == ErrorKind.Cancellation;

        private ErrorWrapper(TError error)
        {
            Error = error;
            _kind = ErrorKind.Failure;
        }

        private ErrorWrapper(ErrorKind cancellationKind)
        {
            Debug.Assert(cancellationKind == ErrorKind.Cancellation,
                "This constructor only builds cancellation wrappers. Use the TError ctor for failures.");
            Error = default!;
            _kind = cancellationKind;
        }

        /// <summary>
        /// Attempts to get the error, failing if it is a cancellation.
        /// </summary>
        public bool TryGetError([MaybeNullWhen(true)] out TError error)
        {
            if (IsCancellationError)
            {
                error = default!;
                return false;
            }
            else
            {
                error = Error;
                return true;
            }
        }

        public static ErrorWrapper<TError> FromError(TError error) => new(error);
        public static ErrorWrapper<TError> FromCancellation() => new(ErrorKind.Cancellation);

        public static implicit operator ErrorWrapper<TError>(TError error) => new(error);
        public static implicit operator ErrorWrapper<TError>(OperationCanceledException _) => new(ErrorKind.Cancellation);
    }

    /// <summary>
    /// Outcome of a fallible operation that yields a value on success, carrying a
    /// custom error type. Encodes exactly one of three states — <b>success</b>
    /// (<see cref="IsSuccess"/>, <see cref="Value"/> is set), <b>failure</b>
    /// (<see cref="IsFailure"/>, <see cref="Error"/> holds a <typeparamref name="TError"/>),
    /// or <b>cancellation</b> (<see cref="IsCanceled"/>) — so callers can branch without
    /// exceptions. Inspect via <c>TryGetSuccess</c> / <c>TryGetFailure</c> / <see cref="IsCanceled"/>.
    /// </summary>
    /// <typeparam name="TValue">The value produced on success.</typeparam>
    /// <typeparam name="TError">The error payload produced on failure.</typeparam>
    public readonly struct ValueResult<TValue, TError>
        where TError : notnull
    {
        /// <summary>
        /// A shared instance that represents a canceled result.
        /// </summary>
        public static readonly ValueResult<TValue, TError> Canceled = FromCancellation();

        /// <summary>
        /// If this is a success result.
        /// </summary>
        public readonly bool IsSuccess;

        /// <summary>
        /// If this is a cancellation result.
        /// </summary>
        public bool IsCanceled => Error.IsCancellationError;

        /// <summary>
        /// If this is a failure result. Not true when canceled.
        /// </summary>
        public bool IsFailure => !IsSuccess && !IsCanceled;

        /// <summary>
        /// The success value.
        /// </summary>
        public readonly TValue Value;

        /// <summary>
        /// The error value.
        /// </summary>
        public readonly ErrorWrapper<TError> Error;

        /// <summary>
        /// Attempts to retrieve the value, failing if the result is a failure.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool TryGetSuccess([NotNullWhen(true)] out TValue value)
        {
            if (IsSuccess)
            {
                value = Value!;
                return true;
            }
            else
            {
                value = default!;
                return false;
            }
        }

        /// <summary>
        /// Attempts to retrieve the error, failing if the result is a success.
        /// </summary>
        /// <param name="error"></param>
        /// <returns></returns>
        public bool TryGetFailure([NotNullWhen(true)] out TError error)
        {
            if (IsFailure)
            {
                error = Error.Error;
                return true;
            }
            else
            {
                error = default!;
                return false;
            }
        }

        private ValueResult(TValue value)
        {
            IsSuccess = true;
            Value = value;
            Error = default!;
        }

        private ValueResult(TError error)
        {
            IsSuccess = false;
            Value = default!;
            Error = error;
        }

        // Sentinel ctor for the cancellation state. The int parameter exists only to
        // give this a distinct signature from the success/failure ctors; its value is
        // unused. Reached via FromCancellation()/the shared Canceled instance.
        private ValueResult(int _)
        {
            IsSuccess = false;
            Value = default!;
            Error = ErrorWrapper<TError>.FromCancellation();
        }

        public static ValueResult<TValue, TError> FromValue(TValue value)
        {
            return new ValueResult<TValue, TError>(value);
        }

        public static ValueResult<TValue, TError> FromError(TError error)
        {
            return new ValueResult<TValue, TError>(error);
        }

        public static ValueResult<TValue, TError> FromCancellation()
        {
            return new ValueResult<TValue, TError>(0);
        }

        public static implicit operator ValueResult<TValue, TError>(TValue value) => new(value);
        public static implicit operator ValueResult<TValue, TError>(TError error) => new(error);
    }

    /// <summary>
    /// Outcome of a fallible operation that yields a value on success, using the default
    /// <see cref="ResultError"/> (public + internal message) error type. Encodes exactly
    /// one of three states — <b>success</b> (<see cref="Value"/> is set), <b>failure</b>
    /// (<see cref="Error"/> holds a <see cref="ResultError"/>), or <b>cancellation</b> — so
    /// callers branch without exceptions. Inspect via <c>TryGetSuccess</c> /
    /// <c>TryGetFailure</c> / <c>IsCanceled</c>.
    /// </summary>
    /// <typeparam name="TValue">The value produced on success.</typeparam>
    public readonly struct ValueResult<TValue>
    {
        /// <summary>
        /// A shared instance that represents a canceled result.
        /// </summary>
        public static readonly ValueResult<TValue> Canceled = FromCancellation();

        /// <summary>
        /// If this is a success result.
        /// </summary>
        public readonly bool IsSuccess;

        /// <summary>
        /// If this is a cancellation result.
        /// </summary>
        public bool IsCanceled => Error.IsCancellationError;

        /// <summary>
        /// If this is a failure result. Not true when canceled.
        /// </summary>
        public bool IsFailure => !IsSuccess && !IsCanceled;

        /// <summary>
        /// The success value.
        /// </summary>
        public readonly TValue Value;

        /// <summary>
        /// The error value.
        /// </summary>
        public readonly ErrorWrapper<ResultError> Error;

        /// <summary>
        /// Attempts to retrieve the value, failing if the result is a failure.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool TryGetSuccess([NotNullWhen(true)] out TValue value)
        {
            if (IsSuccess)
            {
                value = Value!;
                return true;
            }
            else
            {
                value = default!;
                return false;
            }
        }

        /// <summary>
        /// Attempts to retrieve the error, failing if the result is a success.
        /// </summary>
        /// <param name="error"></param>
        /// <returns></returns>
        public bool TryGetFailure([NotNullWhen(true)] out ResultError error)
        {
            if (IsFailure)
            {
                error = Error.Error;
                return true;
            }
            else
            {
                error = default!;
                return false;
            }
        }

        private ValueResult(TValue value)
        {
            IsSuccess = true;
            Value = value;
            Error = default!;
        }

        private ValueResult(ResultError error)
        {
            IsSuccess = false;
            Value = default!;
            Error = error;
        }

        private ValueResult(int _)
        {
            IsSuccess = false;
            Value = default!;
            Error = ErrorWrapper<ResultError>.FromCancellation();
        }

        public static ValueResult<TValue> FromValue(TValue value)
        {
            return new ValueResult<TValue>(value);
        }

        public static ValueResult<TValue> FromError(ResultError error)
        {
            return new ValueResult<TValue>(error);
        }

        public static ValueResult<TValue> FromError(string errorMessage)
        {
            return new ValueResult<TValue>(new ResultError(errorMessage));
        }

        public static ValueResult<TValue> FromError(string publicMessage, string internalMessage)
        {
            return new ValueResult<TValue>(new ResultError(publicMessage, internalMessage));
        }

        public static ValueResult<TValue> FromCancellation()
        {
            return new ValueResult<TValue>(0);
        }

        public static implicit operator ValueResult<TValue>(TValue value) => new(value);
        public static implicit operator ValueResult<TValue>(ResultError error) => new(error);
    }

    /// <summary>
    /// A result with no value type and a custom error type.
    /// </summary>
    /// <typeparam name="TError"></typeparam>
    public readonly struct Result<TError>
        where TError : notnull
    {
        /// <summary>
        /// A shared instance that represents a successful result.
        /// </summary>
        public static readonly Result<TError> Success = new(true);

        /// <summary>
        /// A shared instance that represents a canceled result.
        /// </summary>
        public static readonly Result<TError> Canceled = FromCancellation();

        /// <summary>
        /// If this is a success result.
        /// </summary>
        public readonly bool IsSuccess;

        /// <summary>
        /// If this is a cancellation result.
        /// </summary>
        public bool IsCanceled => Error.IsCancellationError;

        /// <summary>
        /// If this is a failure result. Not true when canceled.
        /// </summary>
        public bool IsFailure => !IsSuccess && !IsCanceled;

        /// <summary>
        /// The error value.
        /// </summary>
        public readonly ErrorWrapper<TError> Error;

        /// <summary>
        /// Attempts to retrieve the error, failing if the result is a success.
        /// </summary>
        /// <param name="error"></param>
        /// <returns></returns>
        public bool TryGetFailure([NotNullWhen(true)] out TError error)
        {
            if (IsFailure)
            {
                error = Error.Error;
                return true;
            }
            else
            {
                error = default!;
                return false;
            }
        }

        /// <summary>
        /// Parameterless constructors can't be private in structs, hence this.
        /// </summary>
        /// <param name="_">Ignored</param>
        private Result(bool _)
        {
            IsSuccess = true;
            Error = default!;
        }

        private Result(TError error)
        {
            IsSuccess = false;
            Error = error;
        }

        private Result(int _)
        {
            IsSuccess = false;
            Error = ErrorWrapper<TError>.FromCancellation();
        }

        public static Result<TError> FromError(TError error)
        {
            return new Result<TError>(error);
        }

        public static Result<TError> FromCancellation()
        {
            return new Result<TError>(0);
        }

        public static implicit operator Result<TError>(TError error) => new(error);
    }

    /// <summary>
    /// Outcome of a fallible operation that produces no value, using the default
    /// <see cref="ResultError"/> (public + internal message) error type. Encodes exactly
    /// one of three states — <b>success</b> (<see cref="Success"/>), <b>failure</b>
    /// (<see cref="Error"/> holds a <see cref="ResultError"/>), or <b>cancellation</b> — so
    /// callers branch without exceptions. Inspect via <see cref="IsSuccess"/> /
    /// <c>TryGetFailure</c> / <c>IsCanceled</c>.
    /// </summary>
    public readonly struct Result
    {
        /// <summary>
        /// A shared instance that represents a successful result.
        /// </summary>
        public static readonly Result Success = new(true);

        /// <summary>
        /// A shared instance that represents a canceled result.
        /// </summary>
        public static readonly Result Canceled = FromCancellation();

        /// <summary>
        /// If this is a success result.
        /// </summary>
        public readonly bool IsSuccess;

        /// <summary>
        /// If this is a cancellation result.
        /// </summary>
        public bool IsCanceled => Error.IsCancellationError;

        /// <summary>
        /// If this is a failure result. Not true when canceled.
        /// </summary>
        public bool IsFailure => !IsSuccess && !IsCanceled;

        /// <summary>
        /// The error value.
        /// </summary>
        public readonly ErrorWrapper<ResultError> Error;

        /// <summary>
        /// Attempts to retrieve the error, failing if the result is a success.
        /// </summary>
        /// <param name="error"></param>
        /// <returns></returns>
        public bool TryGetFailure([NotNullWhen(true)] out ResultError error)
        {
            if (IsFailure)
            {
                error = Error.Error;
                return true;
            }
            else
            {
                error = default!;
                return false;
            }
        }

        /// <summary>
        /// Parameterless constructors can't be private in structs, hence this.
        /// </summary>
        /// <param name="_">Ignored</param>
        private Result(bool _)
        {
            IsSuccess = true;
            Error = default!;
        }

        private Result(ResultError error)
        {
            IsSuccess = false;
            Error = error;
        }

        private Result(int _)
        {
            IsSuccess = false;
            Error = ErrorWrapper<ResultError>.FromCancellation();
        }

        public static Result FromError(ResultError error)
        {
            return new Result(error);
        }

        public static Result FromError(string errorMessage)
        {
            return new Result(new ResultError(errorMessage));
        }

        public static Result FromError(string publicMessage, string internalMessage)
        {
            return new Result(new ResultError(publicMessage, internalMessage));
        }

        public static Result FromCancellation()
        {
            return new Result(0);
        }

        public static implicit operator Result(ResultError error) => new(error);
    }
}
