using KnockBox.Extensions.Exceptions;
using System.Collections.Concurrent;

namespace KnockBox.Services.State.Shared
{
    public abstract class AbstractState<TSelf> : IState<TSelf>
        where TSelf : class
    {
        #region Dtos

        private sealed record class Registration(string PropertyName, Func<CancellationToken, Task> UpdateAction, string[]? Dependencies);

        private sealed class SharedUpdate : IDisposable
        {
            private readonly CancellationTokenSource _executionCts = new();
            private int _activeWaiterCount;
            private int _canceledWaiterCount;

            public SharedUpdate(Func<CancellationToken, Task<UpdateResult>> factory)
            {
                Task = factory(_executionCts.Token);
            }

            public Task<UpdateResult> Task { get; }

            public IDisposable RegisterWaiter(CancellationToken callerToken)
            {
                Interlocked.Increment(ref _activeWaiterCount);
                return new WaiterHandle(this, callerToken);
            }

            private void NotifyCanceled(ref int canceledNotified)
            {
                if (Interlocked.Exchange(ref canceledNotified, 1) == 1) return;

                var canceled = Interlocked.Increment(ref _canceledWaiterCount);
                var active = Volatile.Read(ref _activeWaiterCount);

                if (!Task.IsCompleted && canceled >= active)
                {
                    _executionCts.Cancel();
                }
            }

            private void NotifyCompleted(bool wasCanceled)
            {
                if (wasCanceled)
                {
                    Interlocked.Decrement(ref _canceledWaiterCount);
                }

                var active = Interlocked.Decrement(ref _activeWaiterCount);

                if (!Task.IsCompleted)
                {
                    var canceled = Volatile.Read(ref _canceledWaiterCount);
                    if (canceled >= active)
                    {
                        _executionCts.Cancel();
                    }
                }
            }

            public void Dispose() => _executionCts.Dispose();

            private sealed class WaiterHandle : IDisposable
            {
                private readonly SharedUpdate _owner;
                private readonly CancellationToken _token;
                private CancellationTokenRegistration _registration;
                private int _disposed;
                private int _canceledNotified;

                public WaiterHandle(SharedUpdate owner, CancellationToken token)
                {
                    _owner = owner;
                    _token = token;

                    if (token.CanBeCanceled)
                    {
                        _registration = token.Register(() => _owner.NotifyCanceled(ref _canceledNotified));
                    }
                }

                public void Dispose()
                {
                    if (Interlocked.Exchange(ref _disposed, 1) == 1) return;

                    _registration.Dispose();

                    if (_token.IsCancellationRequested)
                    {
                        _owner.NotifyCanceled(ref _canceledNotified);
                    }

                    var wasCanceled = Volatile.Read(ref _canceledNotified) == 1;
                    _owner.NotifyCompleted(wasCanceled);
                }
            }
        }

        #endregion

        #region Properties, Fields, and Events

        private readonly ConcurrentDictionary<string, Registration> _registrations = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, PropertyState> _states = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, SharedUpdate> _inflightUpdates = new(StringComparer.Ordinal);
        private bool _disposed;

        public event IState<TSelf>.PropertyStateChangedDelegate? PropertyStateChanged;

        #endregion

        public PropertyState GetPropertyState(string propertyName)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _states.TryGetValue(propertyName, out var state) ? state : PropertyState.Uninitialized;
        }

        public Task<List<UpdateResult>> UpdatePropertiesAsync(
            CancellationToken ct = default,
            params string[] propertiesToUpdate)
        {
            return UpdatePropertiesAsync(8, ct, propertiesToUpdate);
        }

        public async Task<List<UpdateResult>> UpdatePropertiesAsync(
            int maxParallelUpdates,
            CancellationToken ct = default,
            params string[] propertiesToUpdate)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxParallelUpdates);
            ObjectDisposedException.ThrowIf(_disposed, this);
            ct.ThrowIfCancellationRequested();

            if (propertiesToUpdate is null || propertiesToUpdate.Length == 0) return [];

            using var concurrencySemaphore = new SemaphoreSlim(maxParallelUpdates, maxParallelUpdates);
            var plan = BuildPlan(propertiesToUpdate);

            var tasks = new ConcurrentDictionary<string, Lazy<Task<UpdateResult>>>(StringComparer.Ordinal);
            var sharedTasks = new ConcurrentDictionary<string, Task<UpdateResult>>(StringComparer.Ordinal);

            async Task<UpdateResult> CreateTask(string p)
            {
                var shared = GetOrCreateSharedUpdate(p, concurrencySemaphore, GetTask);
                _ = sharedTasks.TryAdd(p, shared.Task);
                using var waiter = shared.RegisterWaiter(ct);

                try
                {
                    return await shared.Task.WaitAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    return new UpdateResult(p, new OperationCanceledException(ct), PropertyUpdateResult.Canceled);
                }
            }

            Task<UpdateResult> GetTask(string property) =>
                tasks.GetOrAdd(property, p =>
                    new Lazy<Task<UpdateResult>>(() => CreateTask(p), LazyThreadSafetyMode.ExecutionAndPublication)).Value;

            // Ensure every property in the plan is cached so dependencies aren't re-run later.
            foreach (var p in plan)
                _ = GetTask(p);

            // Kick off updates for requested roots, but return results for whole plan
            var rootTasks = propertiesToUpdate.Distinct(StringComparer.Ordinal).Select(GetTask).ToArray();
            await Task.WhenAll(rootTasks).ConfigureAwait(false);

            // Collect results in a stable order (topological plan order)
            var results = new List<UpdateResult>(plan.Count);
            foreach (var p in plan)
                results.Add(await GetTask(p).ConfigureAwait(false));

            // Ensure shared updates finish before disposing the semaphore
            await Task.WhenAll(sharedTasks.Values).ConfigureAwait(false);

            return results;
        }

        /// <summary>
        /// Registers and updater and its dependencies. Dependencies will be updated first before the property itself is updated.
        /// </summary>
        /// <remarks>
        /// Registration is order-sensitive. Make sure dependencies of a property are registered first.
        /// </remarks>
        /// <param name="propertyName"></param>
        /// <param name="updateAction"></param>
        /// <param name="propertyDependencies"></param>
        protected void RegisterUpdater(
            string propertyName,
            Func<CancellationToken, Task> updateAction,
            params string[] propertyDependencies)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            // Validate
            if (string.IsNullOrWhiteSpace(propertyName)) 
                throw new ArgumentException($"Property name can't be null or whitespace.", nameof(propertyName));

            ArgumentNullException.ThrowIfNull(updateAction);

            // Ensure dependencies are registered first
            if (propertyDependencies is not null && propertyDependencies.Length > 0)
            {
                foreach (var dependency in propertyDependencies)
                {
                    if (!_registrations.ContainsKey(dependency))
                        throw new InvalidOperationException($"Dependency '{dependency}' is not registered. Make sure dependencies are registered first.");
                }
            }

            // Register
            var registration = new Registration(propertyName, updateAction, propertyDependencies);

            if (!_registrations.TryAdd(propertyName, registration))
            {
                throw new InvalidOperationException($"A duplicate update registration was made for property '{propertyName}'.");
            }

            SetPropertyStatus(propertyName, PropertyState.Uninitialized);
        }

        #region Helper Methods

        private SharedUpdate GetOrCreateSharedUpdate(
            string propertyName,
            SemaphoreSlim concurrencySemaphore,
            Func<string, Task<UpdateResult>> dependencyResolver)
        {
            while (true)
            {
                if (_inflightUpdates.TryGetValue(propertyName, out var existing))
                {
                    if (!existing.Task.IsCompleted) return existing;

                    _inflightUpdates.TryRemove(propertyName, out _);
                    continue;
                }

                var created = new SharedUpdate(ct => RunUpdateAsync(propertyName, concurrencySemaphore, ct, dependencyResolver));

                if (_inflightUpdates.TryAdd(propertyName, created))
                {
                    _ = created.Task.ContinueWith(_ =>
                    {
                        _inflightUpdates.TryRemove(propertyName, out var _);
                        created.Dispose();
                    }, TaskScheduler.Default);

                    return created;
                }
            }
        }

        private async Task<UpdateResult> RunUpdateAsync(
            string propertyName,
            SemaphoreSlim concurrencySemaphore,
            CancellationToken ct,
            Func<string, Task<UpdateResult>> dependencyResolver)
        {
            try
            {
                if (!_registrations.TryGetValue(propertyName, out var reg))
                {
                    SetPropertyStatus(propertyName, PropertyState.Errored);
                    return new UpdateResult(propertyName, new InvalidOperationException($"No updater registered for property '{propertyName}'."));
                }

                // 1) Await dependencies and capture their results
                var deps = reg.Dependencies ?? [];
                if (deps.Length > 0)
                {
                    var depTasks = deps.Select(dependencyResolver).ToArray();
                    var depResults = await Task.WhenAll(depTasks).ConfigureAwait(false);

                    // 2) Propagate cancellation/errors from dependencies (and skip running this updater)
                    // Cancellation wins over error (common policy).
                    var depFailures = depResults.Where(r => r.Status != PropertyUpdateResult.Succeeded).ToArray();
                    if (depFailures.Length > 0)
                    {
                        PropertyUpdateResult status = depFailures.Any(r => r.Status == PropertyUpdateResult.Canceled)
                            ? PropertyUpdateResult.Canceled
                            : PropertyUpdateResult.Errored;

                        if (status == PropertyUpdateResult.Canceled)
                        {
                            SetPropertyStatus(propertyName, PropertyState.Canceled);
                        }
                        else
                        {
                            SetPropertyStatus(propertyName, PropertyState.Errored);
                        }

                        var exception = new AggregateException(
                            $"Update skipped because dependencies failed: [{string.Join(", ", depFailures.Select(r => $"{r.PropertyName} - {r.Status}"))}]",
                            depFailures.Select(r => r.Exception));
                        return new UpdateResult(propertyName, exception, status);
                    }
                }

                // 3) Dependencies are good; now run this updater with concurrency limiting
                await concurrencySemaphore.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    SetPropertyStatus(propertyName, PropertyState.Updating);
                    await reg.UpdateAction(ct).ConfigureAwait(false);
                    SetPropertyStatus(propertyName, PropertyState.Ready);
                    return new UpdateResult(propertyName);
                }
                finally
                {
                    concurrencySemaphore.Release();
                }
            }
            catch (Exception ex)
            {
                PropertyUpdateResult status;

                if (!ex.TryGetCancellationException(out _))
                {
                    status = PropertyUpdateResult.Errored;
                    SetPropertyStatus(propertyName, PropertyState.Errored);
                }
                else
                {
                    status = PropertyUpdateResult.Canceled;
                    SetPropertyStatus(propertyName, PropertyState.Canceled);
                }

                return new UpdateResult(propertyName, ex, status);
            }
        }

        private List<string> BuildPlan(IEnumerable<string> roots)
        {
            var ordered = new List<string>();
            var tempMarks = new HashSet<string>(StringComparer.Ordinal); // recursion stack
            var permMarks = new HashSet<string>(StringComparer.Ordinal); // fully processed
            List<string>? missingUpdaters = null;

            void Visit(string property)
            {
                if (string.IsNullOrWhiteSpace(property))
                {
                    throw new ArgumentException("Unable to build dependencies as property is null or whitespace.");
                }

                if (permMarks.Contains(property))
                    return;

                if (!tempMarks.Add(property))
                    throw new InvalidOperationException($"Cycle detected in property dependencies at '{property}'.");

                if (_registrations.TryGetValue(property, out var reg))
                {
                    if (reg.Dependencies is not null && reg.Dependencies.Length > 0)
                    {
                        foreach (var dep in reg.Dependencies)
                            Visit(dep);
                    }
                }
                else
                {
                    missingUpdaters ??= new();
                    missingUpdaters.Add(property);
                }

                tempMarks.Remove(property);
                permMarks.Add(property);
                ordered.Add(property);
            }

            foreach (var root in roots)
                Visit(root);

            if (missingUpdaters is not null && missingUpdaters.Count > 0)
            {
                throw new ArgumentException($"No updaters for properties [{string.Join(", ", missingUpdaters)}] are set.");
            }

            return ordered;
        }

        /// <summary>
        /// Sets the status for the property and notifies listeners.
        /// </summary>
        /// <param name="propertyName"></param>
        /// <param name="newState"></param>
        private void SetPropertyStatus(string propertyName, PropertyState newState)
        {
            bool changed = false;
            _states.AddOrUpdate(propertyName,
                _ => { changed = true; return newState; },
                (_, old) => { changed = old != newState; return newState; });

            if (changed) PropertyStateChanged?.Invoke(this, new(propertyName, newState));
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            if (_disposed) return;

            foreach (var update in _inflightUpdates.Values)
                update.Dispose();

            _inflightUpdates.Clear();
            _registrations.Clear();
            _states.Clear();

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
