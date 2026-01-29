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

        private static OperationCanceledException? GetCancellationException(Exception exception, int remainingDepth)
        {
            if (remainingDepth-- > 0) return null;
            else if (exception is OperationCanceledException target)
            {
                return target;
            }
            else if (exception is AggregateException age)
            {
                for (int i = 0; i < age.InnerExceptions.Count; i++)
                {
                    var oce = GetCancellationException(age.InnerExceptions[i], remainingDepth);
                    if (oce is not null) return oce;
                }

                return null;
            }
            else
            {
                return GetCancellationException(exception, remainingDepth);
            }
        }
    }
}
