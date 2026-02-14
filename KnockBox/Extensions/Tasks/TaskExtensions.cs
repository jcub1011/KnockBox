using KnockBox.Extensions.Exceptions;

namespace KnockBox.Extensions.Tasks
{
    public static class TaskExtensions
    {
        /// <summary>
        /// Awaits the action and extracts nested operation canceled exceptions if applicable.
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public static async Task PropogateCancellationAsync(this Task action)
        {
            try
            {
                await action;
            }
            catch (Exception ex)
            {
                if (ex.TryGetCancellationException(out var oce))
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(oce).Throw();
                throw;
            }
        }

        /// <summary>
        /// Awaits the action and extracts nested operation canceled exceptions if applicable.
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public static async Task<TResult> PropogateCancellationAsync<TResult>(this Task<TResult> action)
        {
            try
            {
                return await action;
            }
            catch (Exception ex)
            {
                if (ex.TryGetCancellationException(out var oce))
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(oce).Throw();
                throw;
            }
        }

        /// <summary>
        /// Awaits the action and extracts nested operation canceled exceptions if applicable.
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public static async ValueTask PropogateCancellationAsync(this ValueTask action)
        {
            try
            {
                await action;
            }
            catch (Exception ex)
            {
                if (ex.TryGetCancellationException(out var oce))
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(oce).Throw();
                throw;
            }
        }

        /// <summary>
        /// Awaits the action and extracts nested operation canceled exceptions if applicable.
        /// </summary>
        /// <param name="action"></param>
        /// <returns></returns>
        public static async ValueTask<TResult> PropogateCancellationAsync<TResult>(this ValueTask<TResult> action)
        {
            try
            {
                return await action;
            }
            catch (Exception ex)
            {
                if (ex.TryGetCancellationException(out var oce))
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(oce).Throw();
                throw;
            }
        }
    }
}
