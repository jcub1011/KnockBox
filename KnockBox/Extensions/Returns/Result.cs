using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Extensions.Returns
{
    /// <summary>
    /// A result with a value and a custom error type.
    /// </summary>
    /// <typeparam name="TValue"></typeparam>
    /// <typeparam name="TError"></typeparam>
    public readonly record struct ValueResult<TValue, TError>
        where TError : notnull
    {
        /// <summary>
        /// If this is a success result.
        /// </summary>
        public readonly bool IsSuccess;

        /// <summary>
        /// If this is a failure result.
        /// </summary>
        public bool IsFailure => !IsSuccess;

        /// <summary>
        /// The success value.
        /// </summary>
        public readonly TValue Value;

        /// <summary>
        /// The error value.
        /// </summary>
        public readonly TError Error;

        /// <summary>
        /// Attempts to retrieve the value, failing if the result is a failure.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool TryGetSuccess([MaybeNullWhen(true)] out TValue value)
        {
            if (IsSuccess)
            {
                value = Value;
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
        public bool TryGetFailure([MaybeNullWhen(true)] out TError error)
        {
            if (IsFailure)
            {
                error = Error;
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

        public static ValueResult<TValue, TError> FromValue(TValue value)
        {
            return new ValueResult<TValue, TError>(value);
        }

        public static ValueResult<TValue, TError> FromError(TError error)
        {
            return new ValueResult<TValue, TError>(error);
        }

        public static implicit operator ValueResult<TValue, TError>(TValue value) => new(value);
        public static implicit operator ValueResult<TValue, TError>(TError error) => new(error);
    }

    /// <summary>
    /// A result with a value and the default <see cref="ResultError"/> error type.
    /// </summary>
    /// <typeparam name="TValue"></typeparam>
    public readonly record struct ValueResult<TValue>
    {
        /// <summary>
        /// If this is a success result.
        /// </summary>
        public readonly bool IsSuccess;

        /// <summary>
        /// If this is a failure result.
        /// </summary>
        public bool IsFailure => !IsSuccess;

        /// <summary>
        /// The success value.
        /// </summary>
        public readonly TValue Value;

        /// <summary>
        /// The error value.
        /// </summary>
        public readonly ResultError Error;

        /// <summary>
        /// Attempts to retrieve the value, failing if the result is a failure.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public bool TryGetSuccess([MaybeNullWhen(true)] out TValue value)
        {
            if (IsSuccess)
            {
                value = Value;
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
        public bool TryGetFailure([MaybeNullWhen(true)] out ResultError error)
        {
            if (IsFailure)
            {
                error = Error;
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

        public static implicit operator ValueResult<TValue>(TValue value) => new(value);
        public static implicit operator ValueResult<TValue>(ResultError error) => new(error);
    }

    /// <summary>
    /// A result with no value type and a custom error type.
    /// </summary>
    /// <typeparam name="TError"></typeparam>
    public readonly record struct Result<TError>
        where TError : notnull
    {
        public static readonly Result<TError> Success = new(true);

        /// <summary>
        /// If this is a success result.
        /// </summary>
        public readonly bool IsSuccess;

        /// <summary>
        /// If this is a failure result.
        /// </summary>
        public bool IsFailure => !IsSuccess;

        /// <summary>
        /// The error value.
        /// </summary>
        public readonly TError Error;

        /// <summary>
        /// Attempts to retrieve the error, failing if the result is a success.
        /// </summary>
        /// <param name="error"></param>
        /// <returns></returns>
        public bool TryGetFailure([MaybeNullWhen(true)] out TError error)
        {
            if (IsFailure)
            {
                error = Error;
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

        public static Result<TError> FromError(TError error)
        {
            return new Result<TError>(error);
        }

        public static implicit operator Result<TError>(TError error) => new(error);
    }

    /// <summary>
    /// A result with no value and the default <see cref="ResultError"/> error type.
    /// </summary>
    public readonly record struct Result
    {
        public static readonly Result Success = new(true);

        /// <summary>
        /// If this is a success result.
        /// </summary>
        public readonly bool IsSuccess;

        /// <summary>
        /// If this is a failure result.
        /// </summary>
        public bool IsFailure => !IsSuccess;

        /// <summary>
        /// The error value.
        /// </summary>
        public readonly ResultError Error;

        /// <summary>
        /// Attempts to retrieve the error, failing if the result is a success.
        /// </summary>
        /// <param name="error"></param>
        /// <returns></returns>
        public bool TryGetFailure([MaybeNullWhen(true)] out ResultError error)
        {
            if (IsFailure)
            {
                error = Error;
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

        public static implicit operator Result(ResultError error) => new(error);
    }
}
