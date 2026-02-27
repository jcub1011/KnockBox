using KnockBox.Extensions.Collections;
using KnockBox.Extensions.Disposable;
using System.Collections.Concurrent;

namespace KnockBox.Extensions.Events
{
    /// <summary>
    /// An event manager used to notify large numbers of listeners quickly.
    /// </summary>
    public sealed class TypedThreadSafeEventManager(ILogger? logger = null) : ITypedThreadSafeEventManager
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<Type, ThreadSafeList<Delegate>>> _groups = new(StringComparer.Ordinal);
        private int _disposed;

        public IDisposable Subscribe<TType>(string group, Func<TType, ValueTask> callback)
        {
            ObjectDisposedException.ThrowIf(_disposed == 1, this);
            if (string.IsNullOrWhiteSpace(group)) throw new ArgumentException("Group can't be null or whitespace.", nameof(group));
            ArgumentNullException.ThrowIfNull(callback);

            var typeMap = _groups.GetOrAdd(group, _ => new ConcurrentDictionary<Type, ThreadSafeList<Delegate>>());
            var list = typeMap.GetOrAdd(typeof(TType), _ => []);

            list.Add(callback);
            return new DisposableAction(() => list.Remove(callback));
        }

        public Task NotifyAsync<TType>(string group, TType args)
        {
            ObjectDisposedException.ThrowIf(_disposed == 1, this);
            if (string.IsNullOrWhiteSpace(group)) throw new ArgumentException("Group can't be null or whitespace.", nameof(group));

            if (!_groups.TryGetValue(group, out var typeMap)) return Task.CompletedTask;
            if (!typeMap.TryGetValue(typeof(TType), out var list)) return Task.CompletedTask;

            var callbacks = Snapshot(list);
            if (callbacks.Length == 0) return Task.CompletedTask;

            var tasks = new Task[callbacks.Length];
            var taskCount = 0;

            for (var i = 0; i < callbacks.Length; i++)
            {
                if (callbacks[i] is Func<TType, ValueTask> typedCallback)
                {
                    var task = SafeInvokeAsync(typedCallback, args);
                    if (!task.IsCompletedSuccessfully)
                    {
                        tasks[taskCount++] = task;
                    }
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

        public void Notify<TType>(string group, TType args)
        {
            async Task ExecuteNotifyAsync(string group, TType args)
            {
                try
                {
                    await NotifyAsync(group, args);
                }
                catch (Exception ex)
                {
                    logger?.LogError(ex, "Error notifying subscribers of group [{group}] with args [{args}].", group, args);
                }
            }

            ObjectDisposedException.ThrowIf(_disposed == 1, this);
            if (string.IsNullOrWhiteSpace(group)) throw new ArgumentException("Group can't be null or whitespace.", nameof(group));

            _ = Task.Run(() => ExecuteNotifyAsync(group, args));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

            foreach (var group in _groups.Values)
            {
                foreach (var list in group.Values)
                {
                    list.Dispose();
                }
            }

            _groups.Clear();
            GC.SuppressFinalize(this);
        }

        private static Delegate[] Snapshot(ThreadSafeList<Delegate> list)
        {
            return [.. list];
        }

        private Task SafeInvokeAsync<TType>(Func<TType, ValueTask> callback, TType args)
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
            catch
            {
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