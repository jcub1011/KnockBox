using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Extensions.Returns
{
    public sealed class Result : Result<int, Exception>
    {
        /// <summary>
        /// A shared successful result instance.
        /// </summary>
        public static readonly Result Success = FromValue();

        public Result() : base(0) { }
        public Result(Exception error) : base(error) { }

        public static Result FromError(Exception error) => new(error);
        public static Result<TValue> FromError<TValue>(Exception error) => new(error);
        public static Result<TValue, TError> FromError<TValue, TError>(TError error)
            where TError : notnull => new(error);

        public static Result FromValue() => new();
        public static Result<TValue> FromValue<TValue>(TValue value) => new(value);
        public static Result<TValue, TError> FromError<TValue, TError>(TValue value)
            where TError : notnull => new(value);
    }

    public sealed class Result<TValue> : Result<TValue, Exception>
    {
        public Result(TValue value) : base(value) { }
        internal Result(Exception error) : base(error) { }
    }

    public class Result<TValue, TError>
        where TError : notnull
    {
        /// <summary>
        /// If this is a success result.
        /// </summary>
        public bool IsSuccess { get; }

        /// <summary>
        /// If this is a failure result.
        /// </summary>
        public bool IsFailure => !IsSuccess;

        private readonly TValue _value;
        private readonly TError _error;

        /// <summary>
        /// The success result.
        /// </summary>
        public TValue Value => IsSuccess ? _value
            : throw new InvalidOperationException("No value.");

        /// <summary>
        /// The error result.
        /// </summary>
        public TError Error => IsFailure ? _error
            : throw new InvalidOperationException("No error.");

        /// <summary>
        /// Gets the success value of the result.
        /// </summary>
        /// <param name="value"></param>
        /// <returns>False if the result is a failure.</returns>
        public bool TryGetValue([MaybeNullWhen(false)] out TValue value)
        {
            if (IsSuccess)
            {
                value = _value!;
                return true;
            }
            else
            {
                value = default!;
                return false;
            }
        }

        /// <summary>
        /// Gets the error value of the result.
        /// </summary>
        /// <param name="error"></param>
        /// <returns>False if the result is a success.</returns>
        public bool TryGetError([NotNullWhen(true)] out TError error)
        {
            if (IsFailure)
            {
                error = _error!;
                return true;
            }
            else
            {
                error = default!;
                return false;
            }
        }

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        /// <param name="result"></param>
        public Result(TValue result)
        {
            // Result being null is completely valid
            IsSuccess = true;
            _value = result;
            _error = default!;
        }

        /// <summary>
        /// Creates an error result.
        /// </summary>
        /// <param name="error"></param>
        internal Result(TError error)
        {
            ArgumentNullException.ThrowIfNull(error);
            IsSuccess = false;
            _value = default!;
            _error = error;
        }
    }
}
