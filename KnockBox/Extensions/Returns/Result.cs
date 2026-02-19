using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Extensions.Returns
{
    public sealed class Result : Result<int, Exception>
    {
        public Result() : base(0) { }
        public Result(Exception error) : base(error) { }

        public new static Result FromError(Exception error) => new(error);
    }

    public sealed class Result<TValue> : Result<TValue, Exception>
    {
        public Result(TValue value) : base(value) { }
        private Result(Exception error) : base(error) { }

        public new static Result<TValue> FromValue(TValue value) => new(value);
        public new static Result<TValue> FromError(Exception error) => new(error);
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
        protected Result(TError error)
        {
            ArgumentNullException.ThrowIfNull(error);
            IsSuccess = false;
            _value = default!;
            _error = error;
        }

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static Result<TValue, TError> FromValue(TValue value) => new(value);

        /// <summary>
        /// Creates an error result.
        /// </summary>
        /// <param name="error"></param>
        /// <returns></returns>
        public static Result<TValue, TError> FromError(TError error) => new(error);
    }
}
