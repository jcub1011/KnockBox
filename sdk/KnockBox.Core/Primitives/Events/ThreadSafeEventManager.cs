using KnockBox.Core.Primitives.Disposable;

namespace KnockBox.Core.Primitives.Events
{
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
                _listeners = [.. _listeners, callback];
            }

            return new DisposableAction(() =>
            {
                lock (_lock)
                {
                    var index = Array.IndexOf(_listeners, callback);
                    if (index >= 0)
                    {
                        var newListeners = new Func<ValueTask>[_listeners.Length - 1];
                        Array.Copy(_listeners, 0, newListeners, 0, index);
                        Array.Copy(_listeners, index + 1, newListeners, index, _listeners.Length - index - 1);
                        _listeners = newListeners;
                    }
                }
            });
        }

        public Task NotifyAsync()
        {
            Func<ValueTask>[] snapshot;

            lock (_lock)
            {
                snapshot = _listeners;
            }

            if (snapshot.Length == 0) return Task.CompletedTask;

            Task[]? tasks = null;
            var taskCount = 0;

            for (var i = 0; i < snapshot.Length; i++)
            {
                var task = SafeInvokeAsync(snapshot[i]);
                if (!task.IsCompletedSuccessfully)
                {
                    tasks ??= new Task[snapshot.Length];
                    tasks[taskCount++] = task;
                }
            }

            if (taskCount == 0) return Task.CompletedTask;
            if (taskCount == 1) return tasks![0];

            if (taskCount != tasks!.Length)
            {
                Array.Resize(ref tasks, taskCount);
            }

            return Task.WhenAll(tasks);
        }

        public void Notify()
        {
            async Task ExecuteNotifyAsync()
            {
                try
                {
                    await NotifyAsync();
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Error notifying subscribers.");
                }
            }

            _ = Task.Run(() => ExecuteNotifyAsync());
        }

        private Task SafeInvokeAsync(Func<ValueTask> callback)
        {
            try
            {
                var valueTask = callback();
                if (valueTask.IsCompletedSuccessfully)
                {
                    return Task.CompletedTask;
                }

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
            try
            {
                await valueTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error notifying subscriber.");
            }
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
                _listeners = [.. _listeners, callback];
            }

            return new DisposableAction(() =>
            {
                lock (_lock)
                {
                    var index = Array.IndexOf(_listeners, callback);
                    if (index >= 0)
                    {
                        var newListeners = new Func<TEventArgs, ValueTask>[_listeners.Length - 1];
                        Array.Copy(_listeners, 0, newListeners, 0, index);
                        Array.Copy(_listeners, index + 1, newListeners, index, _listeners.Length - index - 1);
                        _listeners = newListeners;
                    }
                }
            });
        }

        public Task NotifyAsync(TEventArgs args)
        {
            Func<TEventArgs, ValueTask>[] snapshot;

            lock (_lock)
            {
                snapshot = _listeners;
            }

            if (snapshot.Length == 0) return Task.CompletedTask;

            Task[]? tasks = null;
            var taskCount = 0;

            for (var i = 0; i < snapshot.Length; i++)
            {
                var task = SafeInvokeAsync(snapshot[i], args);
                if (!task.IsCompletedSuccessfully)
                {
                    tasks ??= new Task[snapshot.Length];
                    tasks[taskCount++] = task;
                }
            }

            if (taskCount == 0) return Task.CompletedTask;
            if (taskCount == 1) return tasks![0];

            if (taskCount != tasks!.Length)
            {
                Array.Resize(ref tasks, taskCount);
            }

            return Task.WhenAll(tasks);
        }

        public void Notify(TEventArgs args)
        {
            async Task ExecuteNotifyAsync(TEventArgs args)
            {
                try
                {
                    await NotifyAsync(args);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Error notifying subscribers with args [{args}].", args);
                }
            }

            _ = Task.Run(() => ExecuteNotifyAsync(args));
        }

        private Task SafeInvokeAsync(Func<TEventArgs, ValueTask> callback, TEventArgs args)
        {
            try
            {
                var valueTask = callback(args);
                if (valueTask.IsCompletedSuccessfully)
                {
                    return Task.CompletedTask;
                }

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
            try
            {
                await valueTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error notifying subscriber.");
            }
        }
    }
}
