using KnockBox.Core.Primitives.Disposable;

namespace KnockBox.Core.Primitives.Events
{
    /// <summary>
    /// Shared dispatch helpers for <see cref="ThreadSafeEventManager"/> /
    /// <see cref="ThreadSafeEventManager{TEventArgs}"/>. Snapshot reads are lock-free;
    /// subscribe/unsubscribe build the new snapshot via <see cref="Array.Copy"/> to
    /// avoid intermediate List/enumerator allocations from spread expressions.
    /// </summary>
    internal static class ThreadSafeEventManagerHelper
    {
        public static T[] AddListener<T>(T[] listeners, T callback) where T : Delegate
        {
            var newListeners = new T[listeners.Length + 1];
            if (listeners.Length > 0)
                Array.Copy(listeners, newListeners, listeners.Length);
            newListeners[listeners.Length] = callback;
            return newListeners;
        }

        public static T[] RemoveListener<T>(T[] listeners, T callback) where T : Delegate
        {
            int index = Array.IndexOf(listeners, callback);
            if (index < 0) return listeners;
            if (listeners.Length == 1) return [];

            var newListeners = new T[listeners.Length - 1];
            if (index > 0)
                Array.Copy(listeners, 0, newListeners, 0, index);
            if (index < listeners.Length - 1)
                Array.Copy(listeners, index + 1, newListeners, index, listeners.Length - index - 1);
            return newListeners;
        }

        public static Task DispatchAsync<TListener>(
            TListener[] snapshot,
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
    /// are held in an immutable snapshot array swapped under a lock; notifications
    /// read the snapshot without holding the lock, so handlers can safely
    /// re-enter (subscribe / unsubscribe / notify) without deadlocking.
    /// </summary>
    /// <remarks>
    /// <see cref="Notify"/> is fire-and-forget: it dispatches to all subscribers
    /// on the thread pool and swallows/logs exceptions per-handler. Use
    /// <see cref="NotifyAsync"/> if the caller needs to await completion.
    /// </remarks>
    public sealed class ThreadSafeEventManager(ILogger? logger = null)
        : IThreadSafeEventManager
    {
        private readonly Lock _lock = new();
        private Func<ValueTask>[] _listeners = [];

        public IDisposable Subscribe(Func<ValueTask> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);

            lock (_lock)
            {
                _listeners = ThreadSafeEventManagerHelper.AddListener(_listeners, callback);
            }

            return new DisposableAction(() =>
            {
                lock (_lock)
                {
                    _listeners = ThreadSafeEventManagerHelper.RemoveListener(_listeners, callback);
                }
            });
        }

        public Task NotifyAsync()
        {
            Func<ValueTask>[] snapshot;
            lock (_lock) { snapshot = _listeners; }
            return ThreadSafeEventManagerHelper.DispatchAsync(snapshot, SafeInvokeAsync);
        }

        public void Notify()
        {
            _ = Task.Run(async () =>
            {
                try { await NotifyAsync(); }
                catch (Exception ex) { logger?.LogError(ex, "Error notifying subscribers."); }
            });
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
        private readonly Lock _lock = new();
        private Func<TEventArgs, ValueTask>[] _listeners = [];

        public IDisposable Subscribe(Func<TEventArgs, ValueTask> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);

            lock (_lock)
            {
                _listeners = ThreadSafeEventManagerHelper.AddListener(_listeners, callback);
            }

            return new DisposableAction(() =>
            {
                lock (_lock)
                {
                    _listeners = ThreadSafeEventManagerHelper.RemoveListener(_listeners, callback);
                }
            });
        }

        public Task NotifyAsync(TEventArgs args)
        {
            Func<TEventArgs, ValueTask>[] snapshot;
            lock (_lock) { snapshot = _listeners; }
            return ThreadSafeEventManagerHelper.DispatchAsync(snapshot, cb => SafeInvokeAsync(cb, args));
        }

        public void Notify(TEventArgs args)
        {
            _ = Task.Run(async () =>
            {
                try { await NotifyAsync(args); }
                catch (Exception ex) { logger?.LogError(ex, "Error notifying subscribers with args [{args}].", args); }
            });
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
