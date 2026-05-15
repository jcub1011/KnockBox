namespace KnockBox.Core.Primitives.ThreadSafety
{
    /// <summary>
    /// A lightweight async-aware mutex with single-owner semantics. Replaces
    /// <c>SemaphoreSlim(1, 1)</c> in places that only need a strictly-binary
    /// async lock, at roughly half the per-instance footprint and zero
    /// allocations on the uncontended fast path.
    /// </summary>
    /// <remarks>
    /// <para><b>Fast path:</b> when the mutex is free, <see cref="WaitAsync(CancellationToken)"/>
    /// returns <see cref="ValueTask.CompletedTask"/> after a short <c>lock</c> scope —
    /// no <see cref="TaskCompletionSource"/>, no waiter queue allocated.</para>
    /// <para><b>Slow path:</b> contended waits enqueue a
    /// <see cref="TaskCompletionSource"/> into a lazily-allocated FIFO queue
    /// (<c>null</c> until first contention). <see cref="Release"/> hands the
    /// lock to the next non-canceled waiter; canceled waiters are skipped.</para>
    /// <para><b>Cancellation:</b> a per-wait
    /// <see cref="CancellationTokenRegistration"/> marks the queued
    /// <see cref="TaskCompletionSource"/> canceled on token fire. The waiter
    /// remains physically in the queue until <see cref="Release"/> reaches it
    /// and observes the canceled state — no per-cancellation queue rebuild.
    /// Once the returned <see cref="ValueTask"/> completes successfully, the
    /// caller owns the lock and is responsible for <see cref="Release"/> — even
    /// if the supplied cancellation token fires afterwards.</para>
    /// <para><b>FIFO:</b> non-canceled waiters are served strictly in the
    /// order they called <see cref="WaitAsync(CancellationToken)"/>.</para>
    /// <para><b>Disposal:</b> all queued waiters complete with
    /// <see cref="ObjectDisposedException"/>; subsequent
    /// <c>Wait</c> / <c>WaitAsync</c> calls do the same.</para>
    /// <para>Intended for internal Core use (the per-state execute lock in
    /// <c>AbstractGameState</c>, the per-update semaphore in
    /// <c>AbstractState</c>). Not exported as a public primitive.</para>
    /// </remarks>
    internal sealed class AsyncMutex : IDisposable
    {
        private readonly Lock _gate = new();
        // Lazy: stays null until the first contended Wait. Avoids ~40 bytes of
        // Queue<T> baseline on every state.
        private Queue<TaskCompletionSource>? _waiters;
        private bool _held;
        private bool _disposed;

        /// <summary>
        /// Acquires the mutex synchronously, blocking the calling thread if
        /// the lock is held. Throws <see cref="ObjectDisposedException"/> if
        /// the mutex has been disposed.
        /// </summary>
        public void Wait()
        {
            var vt = WaitAsync(CancellationToken.None);
            if (vt.IsCompletedSuccessfully) return;
            vt.AsTask().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Acquires the mutex asynchronously. The returned
        /// <see cref="ValueTask"/> completes when the lock is held by the
        /// caller. Cancellation completes the wait with
        /// <see cref="OperationCanceledException"/> without acquiring the lock.
        /// </summary>
        public ValueTask WaitAsync(CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return ValueTask.FromCanceled(ct);

            TaskCompletionSource tcs;
            lock (_gate)
            {
                if (_disposed) return ValueTask.FromException(new ObjectDisposedException(nameof(AsyncMutex)));

                if (!_held)
                {
                    _held = true;
                    return ValueTask.CompletedTask;
                }

                _waiters ??= new Queue<TaskCompletionSource>();
                tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _waiters.Enqueue(tcs);
            }

            if (!ct.CanBeCanceled) return new ValueTask(tcs.Task);

            var reg = ct.Register(static s => ((TaskCompletionSource)s!).TrySetCanceled(), tcs);
            return AwaitWithRegistrationAsync(tcs, reg);
        }

        private static async ValueTask AwaitWithRegistrationAsync(TaskCompletionSource tcs, CancellationTokenRegistration reg)
        {
            try { await tcs.Task.ConfigureAwait(false); }
            finally { reg.Dispose(); }
        }

        /// <summary>
        /// Releases the lock. Hands ownership to the next non-canceled waiter
        /// if any; otherwise marks the mutex free. Calling <c>Release</c>
        /// without holding the lock throws <see cref="InvalidOperationException"/>.
        /// </summary>
        public void Release()
        {
            while (true)
            {
                TaskCompletionSource? next = null;
                lock (_gate)
                {
                    ObjectDisposedException.ThrowIf(_disposed, this);
                    if (!_held) throw new InvalidOperationException("AsyncMutex released without being held.");

                    if (_waiters is not null && _waiters.Count > 0)
                    {
                        next = _waiters.Dequeue();
                    }
                    else
                    {
                        _held = false;
                        return;
                    }
                }

                // Outside the lock: hand ownership to this waiter. If it was
                // already canceled (token fired before we picked it), loop and
                // pick the next one. _held stays true until we either find a
                // live waiter or empty the queue.
                if (next.TrySetResult()) return;
            }
        }

        public void Dispose()
        {
            Queue<TaskCompletionSource>? toFail;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                toFail = _waiters;
                _waiters = null;
            }

            if (toFail is null) return;
            var ex = new ObjectDisposedException(nameof(AsyncMutex));
            foreach (var tcs in toFail) tcs.TrySetException(ex);
        }
    }
}
