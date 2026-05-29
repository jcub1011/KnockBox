using KnockBox.Core.Primitives.Disposable;
using System.Collections.Immutable;

namespace KnockBox.Core.Primitives.Events
{
    /// <summary>
    /// Shared dispatch helper for <see cref="ThreadSafeEventManager"/> /
    /// <see cref="ThreadSafeEventManager{TEventArgs}"/>. Iterates a snapshot of
    /// listeners and awaits only the listeners that didn't complete synchronously,
    /// avoiding <see cref="Task.WhenAll(Task[])"/> overhead when every handler is
    /// already done. Subscribe/unsubscribe is handled by each manager directly via
    /// <see cref="ImmutableArray{T}"/> Add/Remove under the manager's own lock.
    /// </summary>
    internal static class ThreadSafeEventManagerHelper
    {
        public static Task DispatchAsync<TListener>(
            ImmutableArray<TListener> snapshot,
            Func<TListener, Task> invoker)
        {
            if (snapshot.Length == 0) return Task.CompletedTask;

            Task[]? tasks = null;
            var taskCount = 0;

            for (var i = 0; i < snapshot.Length; i++)
            {
                var task = invoker(snapshot[i]);
                if (!task.IsCompletedSuccessfully)
                {
                    tasks ??= new Task[snapshot.Length];
                    tasks[taskCount++] = task;
                }
            }

            if (taskCount == 0) return Task.CompletedTask;
            if (taskCount == 1) return tasks![0];

            if (taskCount != tasks!.Length)
                Array.Resize(ref tasks, taskCount);

            return Task.WhenAll(tasks);
        }
    }

    /// <summary>
    /// Default <see cref="IThreadSafeEventManager"/> implementation. Subscribers
    /// are held in an <see cref="ImmutableArray{T}"/> snapshot swapped under a lock;
    /// notifications read the snapshot without holding the lock, so handlers can
    /// safely re-enter (subscribe / unsubscribe / notify) without deadlocking.
    /// </summary>
    /// <remarks>
    /// <see cref="Notify"/> is fire-and-forget: it dispatches to all subscribers
    /// on the thread pool and swallows/logs exceptions per-handler. Use
    /// <see cref="NotifyAsync"/> if the caller needs to await completion.
    /// </remarks>
    public sealed class ThreadSafeEventManager(ILogger? logger = null)
        : IThreadSafeEventManager
    {
        private ImmutableArray<Func<ValueTask>> _listeners = [];

        public IDisposable Subscribe(Func<ValueTask> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);

            ImmutableInterlocked.Update(ref _listeners, static (list, cb) => list.Add(cb), callback);

            return new DisposableAction(() =>
                ImmutableInterlocked.Update(ref _listeners, static (list, cb) => list.Remove(cb), callback));
        }

        /// <summary>
        /// Drops every subscriber. Owners (e.g. <c>AbstractGameState.Dispose</c>) call
        /// this to release captured component/engine references promptly instead of
        /// waiting for GC to break the owner↔subscriber cycle. Atomic against concurrent
        /// <see cref="Subscribe"/> / <see cref="Notify"/>.
        /// </summary>
        public void Clear() =>
            ImmutableInterlocked.InterlockedExchange(ref _listeners, ImmutableArray<Func<ValueTask>>.Empty);

        // Bare read is safe: ImmutableArray<T> is a single-reference struct and writes
        // via ImmutableInterlocked.Update use Interlocked.CompareExchange (full barrier).
        // We can't Volatile.Read directly — its T : class constraint excludes value types.
        public Task NotifyAsync() =>
            ThreadSafeEventManagerHelper.DispatchAsync(_listeners, SafeInvokeAsync);

        /// <summary>
        /// Dispatches the notification. Sync handlers run on the calling thread;
        /// only pending async handlers are awaited fire-and-forget. The
        /// zero-subscriber and all-sync paths allocate nothing.
        /// <para><b>Lock-discipline contract:</b> when this manager belongs to an
        /// <c>AbstractGameState</c> (or any state that serializes mutations through
        /// its own lock), <c>Notify</c> must only be called <i>after</i> that lock
        /// has been released. Subscribers commonly call Blazor <c>InvokeAsync</c> +
        /// <c>StateHasChanged</c>; the resulting renderer work runs synchronously
        /// on the calling dispatcher, and doing that while the state lock is held
        /// will deadlock. See <c>knockbox-platform-architecture.md</c>
        /// (Concurrency → "Notify outside the lock").</para>
        /// </summary>
        public void Notify()
        {
            // See NotifyAsync for the bare-read rationale.
            var snapshot = _listeners;
            if (snapshot.Length == 0) return;

            for (int i = 0; i < snapshot.Length; i++)
            {
                ValueTask vt;
                try { vt = snapshot[i](); }
                catch (Exception ex) { logger?.LogError(ex, "Error notifying subscriber."); continue; }

                if (vt.IsCompletedSuccessfully) continue;
                _ = AwaitValueTaskAsync(vt);
            }
        }

        private Task SafeInvokeAsync(Func<ValueTask> callback)
        {
            try
            {
                var valueTask = callback();
                if (valueTask.IsCompletedSuccessfully) return Task.CompletedTask;
                return AwaitValueTaskAsync(valueTask);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error notifying subscriber.");
                return Task.CompletedTask;
            }
        }

        private async Task AwaitValueTaskAsync(ValueTask valueTask)
        {
            try { await valueTask.ConfigureAwait(false); }
            catch (Exception ex) { logger?.LogError(ex, "Error notifying subscriber."); }
        }
    }

    /// <summary>
    /// Default <see cref="IThreadSafeEventManager{TEventArgs}"/> implementation
    /// for a single event type with a typed payload. Uses the same snapshot-
    /// without-lock dispatch as the non-generic variant.
    /// </summary>
    /// <typeparam name="TEventArgs">Type of the payload passed to each subscriber.</typeparam>
    public sealed class ThreadSafeEventManager<TEventArgs>(ILogger? logger = null) : IThreadSafeEventManager<TEventArgs>
    {
        private ImmutableArray<Func<TEventArgs, ValueTask>> _listeners = [];

        public IDisposable Subscribe(Func<TEventArgs, ValueTask> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);

            ImmutableInterlocked.Update(ref _listeners, static (list, cb) => list.Add(cb), callback);

            return new DisposableAction(() =>
                ImmutableInterlocked.Update(ref _listeners, static (list, cb) => list.Remove(cb), callback));
        }

        /// <summary>
        /// Drops every subscriber. See the non-generic <see cref="ThreadSafeEventManager.Clear"/>
        /// for the rationale.
        /// </summary>
        public void Clear() =>
            ImmutableInterlocked.InterlockedExchange(ref _listeners, ImmutableArray<Func<TEventArgs, ValueTask>>.Empty);

        // See the non-generic ThreadSafeEventManager for the bare-read rationale.
        public Task NotifyAsync(TEventArgs args) =>
            ThreadSafeEventManagerHelper.DispatchAsync(_listeners, cb => SafeInvokeAsync(cb, args));

        /// <summary>
        /// Dispatches the notification. Sync handlers run on the calling thread;
        /// only pending async handlers are awaited fire-and-forget. The
        /// zero-subscriber and all-sync paths allocate nothing. See the non-generic
        /// <c>Notify()</c> for the lock-discipline contract.
        /// </summary>
        public void Notify(TEventArgs args)
        {
            // See the non-generic ThreadSafeEventManager for the bare-read rationale.
            var snapshot = _listeners;
            if (snapshot.Length == 0) return;

            for (int i = 0; i < snapshot.Length; i++)
            {
                ValueTask vt;
                try { vt = snapshot[i](args); }
                catch (Exception ex) { logger?.LogError(ex, "Error notifying subscriber."); continue; }

                if (vt.IsCompletedSuccessfully) continue;
                _ = AwaitValueTaskAsync(vt);
            }
        }

        private Task SafeInvokeAsync(Func<TEventArgs, ValueTask> callback, TEventArgs args)
        {
            try
            {
                var valueTask = callback(args);
                if (valueTask.IsCompletedSuccessfully) return Task.CompletedTask;
                return AwaitValueTaskAsync(valueTask);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error notifying subscriber.");
                return Task.CompletedTask;
            }
        }

        private async Task AwaitValueTaskAsync(ValueTask valueTask)
        {
            try { await valueTask.ConfigureAwait(false); }
            catch (Exception ex) { logger?.LogError(ex, "Error notifying subscriber."); }
        }
    }
}
