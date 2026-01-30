using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace KnockBox.Extensions
{
    public static class ExceptionExtensions
    {
        /// <summary>
        /// Attempts to get the cancellation exception nested in this exception.
        /// </summary>
        /// <param name="exception"></param>
        /// <param name="oce"></param>
        /// <returns>If the exception was found.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetCancellationException(this Exception exception, [NotNullWhen(true)] out OperationCanceledException? oce)
        {
            oce = exception.GetCancellationException();
            return oce is not null;
        }

        /// <summary>
        /// Gets the cancellation exception nested in this exception.
        /// </summary>
        /// <param name="exception"></param>
        /// <returns>Null if not found.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static OperationCanceledException? GetCancellationException(this Exception exception)
        {
            return GetCancellationException(exception, 32);
        }

        /// <summary>
        /// Checks for an operation cancelled exception in all of the exceptions in the list.
        /// </summary>
        /// <param name="exceptions"></param>
        /// <returns></returns>
        public static OperationCanceledException? GetCancellationException(this IEnumerable<Exception> exceptions)
        {
            foreach (var exception in exceptions)
            {
                var result = exception.GetCancellationException();
                if (result is not null) return result;
            }

            return null;
        }

        private static OperationCanceledException? GetCancellationException(Exception? exception, int remainingDepth)
        {
            if (remainingDepth-- <= 0 || exception is null) return null;
            
            if (exception is OperationCanceledException oce)
            {
                return oce;
            }
            
            if (exception is AggregateException age)
            {
                for (int i = 0; i < age.InnerExceptions.Count; i++)
                {
                    var found = GetCancellationException(age.InnerExceptions[i], remainingDepth);
                    if (found is not null) return found;
                }

                return null;
            }

            return GetCancellationException(exception.InnerException, remainingDepth);
        }
    }
}
