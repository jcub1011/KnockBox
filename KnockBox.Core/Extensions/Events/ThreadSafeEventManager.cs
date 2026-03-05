using KnockBox.Extensions.Disposable;

namespace KnockBox.Extensions.Events
{
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

            var tasks = new Task[snapshot.Length];
            var taskCount = 0;

            for (var i = 0; i < snapshot.Length; i++)
            {
                var task = SafeInvokeAsync(snapshot[i]);
                if (!task.IsCompletedSuccessfully)
                {
                    tasks[taskCount++] = task;
                }
            }

            if (taskCount == 0) return Task.CompletedTask;
            if (taskCount == 1) return tasks[0];

            if (taskCount != tasks.Length)
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
    /// A thread-safe event manager for a single event type.
    /// </summary>
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

            var tasks = new Task[snapshot.Length];
            var taskCount = 0;

            for (var i = 0; i < snapshot.Length; i++)
            {
                var task = SafeInvokeAsync(snapshot[i], args);
                if (!task.IsCompletedSuccessfully)
                {
                    tasks[taskCount++] = task;
                }
            }

            if (taskCount == 0) return Task.CompletedTask;
            if (taskCount == 1) return tasks[0];

            if (taskCount != tasks.Length)
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
