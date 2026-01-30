using KnockBox.Extensions;
using System.Collections.Concurrent;

namespace KnockBox.Services.State.Shared
{
    public abstract class AbstractState<TSelf> : IState<TSelf>
        where TSelf : class
    {
        #region Dtos

        private sealed record class Registration(string PropertyName, Func<CancellationToken, Task> UpdateAction, string[]? Dependencies);

        #endregion

        #region Properties, Fields, and Events

        private readonly ConcurrentDictionary<string, Registration> _registrations = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, PropertyState> _states = new(StringComparer.Ordinal);
        private readonly HashSet<string> _lockedProperties = new(StringComparer.Ordinal);
        private readonly Lock _lockGate = new();
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
            var planSet = plan.ToHashSet(StringComparer.Ordinal);
            LockProperties(planSet);

            try
            {
                var tasks = new ConcurrentDictionary<string, Task<UpdateResult>>(StringComparer.Ordinal);

                Task<UpdateResult> GetTask(string property) =>
                    tasks.GetOrAdd(property, async p =>
                    {
                        try
                        {
                            ct.ThrowIfCancellationRequested();

                            if (!_registrations.TryGetValue(p, out var reg))
                            {
                                SetPropertyStatus(p, PropertyState.Errored);
                                return new UpdateResult(p, new InvalidOperationException($"No updater registered for property '{p}'."));
                            }

                            // 1) Await dependencies and capture their results
                            var deps = reg.Dependencies ?? [];
                            var depProps = deps.Where(planSet.Contains).Distinct(StringComparer.Ordinal).ToArray();

                            if (depProps.Length > 0)
                            {
                                var depTasks = depProps.Select(GetTask).ToArray();
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
                                        SetPropertyStatus(p, PropertyState.Canceled);
                                    }
                                    else
                                    {
                                        SetPropertyStatus(p, PropertyState.Errored);
                                    }

                                    var exception = new AggregateException(
                                        $"Update skipped because dependencies failed: [{string.Join(", ", depFailures.Select(r => $"{r.PropertyName} - {r.Status}"))}]", 
                                        depFailures.Select(r => r.Exception));
                                    return new UpdateResult(p, exception, status);
                                }
                            }

                            // 3) Dependencies are good; now run this updater with concurrency limiting
                            await concurrencySemaphore.WaitAsync(ct).ConfigureAwait(false);
                            try
                            {
                                SetPropertyStatus(p, PropertyState.Updating);
                                await reg.UpdateAction(ct).ConfigureAwait(false);
                                SetPropertyStatus(p, PropertyState.Ready);
                                return new UpdateResult(p);
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
                                SetPropertyStatus(p, PropertyState.Errored);
                            }
                            else
                            {
                                status = PropertyUpdateResult.Canceled;
                                SetPropertyStatus(p, PropertyState.Canceled);
                            }

                            return new UpdateResult(p, ex, status);
                        }
                    });

                // Kick off updates for requested roots, but return results for whole plan
                var rootTasks = propertiesToUpdate.Distinct(StringComparer.Ordinal).Select(GetTask).ToArray();
                await Task.WhenAll(rootTasks).ConfigureAwait(false);

                // Collect results in a stable order (topological plan order)
                var results = new List<UpdateResult>(plan.Count);
                foreach (var p in plan)
                    results.Add(await GetTask(p).ConfigureAwait(false));
                return results;
            }
            finally
            {
                ReleaseProperties(planSet);
            }
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

        /// <summary>
        /// Attempts to lock the properties, throwing an exception if any of the properties are already locked.
        /// </summary>
        /// <param name="propertyNames"></param>
        private void LockProperties(IEnumerable<string> propertyNames)
        {
            using (_lockGate.EnterScope())
            {
                List<string> lockedProperties = [.. propertyNames.Where(_lockedProperties.Contains)];
                if (lockedProperties.Count > 0)
                    throw new InvalidOperationException($"Unable to update properties [{string.Join(", ", lockedProperties)}] as they are already being updated.");

                foreach (var property in propertyNames)
                {
                    _lockedProperties.Add(property);
                }
            }
        }

        /// <summary>
        /// Releases the lock on the properties.
        /// </summary>
        /// <param name="propertyNames"></param>
        private void ReleaseProperties(IEnumerable<string> propertyNames)
        {
            using (_lockGate.EnterScope())
            {
                foreach (var property in propertyNames)
                {
                    _lockedProperties.Remove(property);
                }
            }
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            if (_disposed) return;

            _registrations.Clear();
            _states.Clear();
            _lockedProperties.Clear();

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        #endregion
    }
}
