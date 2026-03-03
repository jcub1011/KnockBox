using System.Diagnostics.CodeAnalysis;

namespace KnockBox.Extensions.Returns
{
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

        public ValueResult(TValue value)
        {
            IsSuccess = true;
            Value = value;
            Error = default!;
        }

        public ValueResult(TError error)
        {
            IsSuccess = false;
            Value = default!;
            Error = error;
        }

        public static ValueResult<TVal, TErr> FromValue<TVal, TErr>(TVal value)
            where TErr : notnull
        {
            return new ValueResult<TVal, TErr>(value);
        }

        public static ValueResult<TVal, TErr> FromError<TVal, TErr>(TErr error)
            where TErr : notnull
        {
            return new ValueResult<TVal, TErr>(error);
        }
    }

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

        public ValueResult(TValue value)
        {
            IsSuccess = true;
            Value = value;
            Error = default!;
        }

        public ValueResult(ResultError error)
        {
            IsSuccess = false;
            Value = default!;
            Error = error;
        }

        public static ValueResult<TVal> FromValue<TVal>(TVal value)
        {
            return new ValueResult<TVal>(value);
        }

        public static ValueResult<TVal> FromError<TVal>(ResultError error)
        {
            return new ValueResult<TVal>(error);
        }

        public static ValueResult<TVal> FromError<TVal>(string errorMessage)
        {
            return new ValueResult<TVal>(new ResultError(errorMessage));
        }

        public static ValueResult<TVal> FromError<TVal>(string publicMessage, string internalMessage)
        {
            return new ValueResult<TVal>(new ResultError(publicMessage, internalMessage));
        }
    }

    public readonly record struct Result<TError>
        where TError : notnull
    {
        public static readonly Result<TError> Success = new();

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

        public Result()
        {
            IsSuccess = true;
            Error = default!;
        }

        public Result(TError error)
        {
            IsSuccess = false;
            Error = error;
        }

        public static Result<TErr> FromError<TErr>(TErr error)
            where TErr : notnull
        {
            return new Result<TErr>(error);
        }
    }

    public readonly record struct Result
    {
        public static readonly Result Success = new();

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

        public Result()
        {
            IsSuccess = true;
            Error = default!;
        }

        public Result(ResultError error)
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
    }
}
