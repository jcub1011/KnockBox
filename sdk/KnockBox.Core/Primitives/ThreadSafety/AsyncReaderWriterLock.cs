namespace KnockBox.Core.Primitives.ThreadSafety
{
    /// <summary>
    /// A lightweight async-aware reader/writer lock. Multiple concurrent
    /// readers are admitted; writers are exclusive against both readers and
    /// other writers. Writer-preferring: a reader called while any writer is
    /// queued waits behind that writer.
    /// </summary>
    /// <remarks>
    /// <para><b>Fast path:</b> when no writer is held or queued,
    /// <see cref="WaitReadAsync(CancellationToken)"/> bumps the reader count
    /// and returns <see cref="ValueTask.CompletedTask"/> after a short
    /// <c>lock</c> scope. <see cref="WaitWriteAsync(CancellationToken)"/>
    /// has the same fast path when there are no active readers and no
    /// writer is held. No waiter node is allocated on the uncontended path.</para>
    /// <para><b>Slow path:</b> contended waiters enqueue a polymorphic
    /// <see cref="WaiterNode"/> (sync or async flavor) into lazily-allocated
    /// FIFO queues (one for readers, one for writers; both <c>null</c> until
    /// first contention). The promotion routine prefers writers when readers
    /// have just released; when a writer releases, the next queued writer
    /// wins over any queued reader. Async waiters wrap a
    /// <see cref="TaskCompletionSource"/>; sync waiters wrap a
    /// <see cref="ManualResetEventSlim"/> so the calling thread parks on the
    /// kernel event instead of allocating a <see cref="Task"/> and blocking
    /// on <c>.GetAwaiter().GetResult()</c>.</para>
    /// <para><b>Cancellation:</b> async waits accept a
    /// <see cref="CancellationToken"/>; a per-wait
    /// <see cref="CancellationTokenRegistration"/> marks the queued waiter
    /// canceled on token fire. The dequeue loop on promotion skips canceled
    /// waiters and refunds the reader/writer slot it provisionally claimed
    /// for them. Sync waits do not take a token (the public
    /// <c>WaitRead</c> / <c>WaitWrite</c> APIs have no <c>ct</c> parameter)
    /// and therefore always succeed once dequeued. Once the returned
    /// <see cref="ValueTask"/> completes successfully (async) or
    /// <c>WaitRead</c> / <c>WaitWrite</c> returns (sync), the caller owns the
    /// slot and is responsible for the matching <see cref="ReleaseRead"/> /
    /// <see cref="ReleaseWrite"/>.</para>
    /// <para><b>FIFO:</b> non-canceled waiters within each queue are served
    /// strictly in the order they called <c>Wait*</c>. Writer-preference
    /// applies across the two queues. Sync and async waiters share the same
    /// queues, so they fight for slots in arrival order.</para>
    /// <para><b>Disposal:</b> all queued waiters (both queues) complete with
    /// <see cref="ObjectDisposedException"/>; subsequent
    /// <c>Wait*</c> / <c>Release*</c> calls throw.</para>
    /// <para><b>Reentrancy:</b> not supported. Acquiring a write lock while
    /// already holding either a read or a write lock from the same async
    /// flow will deadlock. The base class' "inside Execute" detection is
    /// done via an <see cref="AsyncLocal{T}"/> marker, not via lock
    /// reentrancy, so this restriction is intentional.</para>
    /// <para>Intended for internal Core use (the per-state execute lock in
    /// <c>AbstractGameState</c>). Not exported as a public primitive.</para>
    /// </remarks>
    internal sealed class AsyncReaderWriterLock : IDisposable
    {
        private readonly Lock _gate = new();
        private int _readers;
        private bool _writerHeld;
        // Lazy: stay null until the first contended Wait. Avoids the
        // baseline Queue<T> allocation on every lock instance.
        private Queue<WaiterNode>? _readerQueue;
        private Queue<WaiterNode>? _writerQueue;
        private bool _disposed;

        /// <summary>
        /// Acquires a shared read lock synchronously, blocking the calling
        /// thread on a <see cref="ManualResetEventSlim"/> if a writer is
        /// currently held or queued. Throws
        /// <see cref="ObjectDisposedException"/> if the lock has been disposed.
        /// </summary>
        public void WaitRead()
        {
            SyncWaiter? sync;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_writerHeld && (_writerQueue is null || _writerQueue.Count == 0))
                {
                    _readers++;
                    return;
                }
                sync = new SyncWaiter(isWriter: false);
                _readerQueue ??= new Queue<WaiterNode>();
                _readerQueue.Enqueue(sync);
            }

            sync.Wait();
        }

        /// <summary>
        /// Acquires the exclusive write lock synchronously, blocking the
        /// calling thread on a <see cref="ManualResetEventSlim"/> if any
        /// reader or writer is currently held. Throws
        /// <see cref="ObjectDisposedException"/> if the lock has been disposed.
        /// </summary>
        public void WaitWrite()
        {
            SyncWaiter? sync;
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_writerHeld && _readers == 0)
                {
                    _writerHeld = true;
                    return;
                }
                sync = new SyncWaiter(isWriter: true);
                _writerQueue ??= new Queue<WaiterNode>();
                _writerQueue.Enqueue(sync);
            }

            sync.Wait();
        }

        /// <summary>
        /// Acquires a shared read lock asynchronously. The returned
        /// <see cref="ValueTask"/> completes when the read slot is held by
        /// the caller. Cancellation completes the wait with
        /// <see cref="OperationCanceledException"/> without acquiring the
        /// slot.
        /// </summary>
        public ValueTask WaitReadAsync(CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return ValueTask.FromCanceled(ct);

            AsyncWaiter waiter;
            lock (_gate)
            {
                if (_disposed) return ValueTask.FromException(new ObjectDisposedException(nameof(AsyncReaderWriterLock)));

                // Writer-preference: queue behind any pending writer even if
                // no writer is currently held, so a continuous stream of
                // readers can't starve a queued writer.
                if (!_writerHeld && (_writerQueue is null || _writerQueue.Count == 0))
                {
                    _readers++;
                    return ValueTask.CompletedTask;
                }

                waiter = new AsyncWaiter(isWriter: false);
                _readerQueue ??= new Queue<WaiterNode>();
                _readerQueue.Enqueue(waiter);
            }

            return waiter.AwaitWithCancellation(ct);
        }

        /// <summary>
        /// Acquires the exclusive write lock asynchronously. The returned
        /// <see cref="ValueTask"/> completes when the write slot is held
        /// by the caller. Cancellation completes the wait with
        /// <see cref="OperationCanceledException"/> without acquiring the
        /// slot.
        /// </summary>
        public ValueTask WaitWriteAsync(CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return ValueTask.FromCanceled(ct);

            AsyncWaiter waiter;
            lock (_gate)
            {
                if (_disposed) return ValueTask.FromException(new ObjectDisposedException(nameof(AsyncReaderWriterLock)));

                if (!_writerHeld && _readers == 0)
                {
                    _writerHeld = true;
                    return ValueTask.CompletedTask;
                }

                waiter = new AsyncWaiter(isWriter: true);
                _writerQueue ??= new Queue<WaiterNode>();
                _writerQueue.Enqueue(waiter);
            }

            return waiter.AwaitWithCancellation(ct);
        }

        /// <summary>
        /// Releases a previously-acquired read lock. Calling this without
        /// holding the read lock throws <see cref="InvalidOperationException"/>.
        /// </summary>
        public void ReleaseRead()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_readers <= 0)
                    throw new InvalidOperationException("AsyncReaderWriterLock.ReleaseRead called without holding a read lock.");
                _readers--;
            }
            TryPromote();
        }

        /// <summary>
        /// Releases a previously-acquired write lock. Calling this without
        /// holding the write lock throws <see cref="InvalidOperationException"/>.
        /// </summary>
        public void ReleaseWrite()
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_writerHeld)
                    throw new InvalidOperationException("AsyncReaderWriterLock.ReleaseWrite called without holding the write lock.");
                _writerHeld = false;
            }
            TryPromote();
        }

        // Drives ownership handoff after a release. Writer-preferring: if a
        // writer is queued and there are no active readers, wake it; else
        // drain a bounded batch of queued readers. Loops past canceled
        // waiters so the lock does not stall on a token-canceled queue
        // entry. The batch size is small (16) so a writer queued mid-drain
        // can still interject between batches.
        private void TryPromote()
        {
            while (true)
            {
                WaiterNode? writerWake = null;
                ReaderBatch batch = default;
                Span<WaiterNode> readers = batch;
                int batchCount = 0;

                lock (_gate)
                {
                    if (_disposed) return;
                    if (_writerHeld) return;

                    if (_readers == 0 && _writerQueue is not null && _writerQueue.Count > 0)
                    {
                        writerWake = _writerQueue.Dequeue();
                        _writerHeld = true;
                    }
                    else if ((_writerQueue is null || _writerQueue.Count == 0)
                             && _readerQueue is not null && _readerQueue.Count > 0)
                    {
                        // Drain up to ReaderBatch.Capacity readers in one
                        // lock acquisition. Bounded so a writer queued
                        // during the drain still gets promoted between
                        // batches.
                        while (batchCount < readers.Length && _readerQueue.TryDequeue(out var w))
                        {
                            readers[batchCount++] = w;
                            _readers++;
                        }
                    }
                    else
                    {
                        return;
                    }
                }

                if (writerWake is not null)
                {
                    if (writerWake.TrySetResult()) return;
                    // The writer's wait was canceled before we could hand
                    // ownership to it; refund the slot and loop to try the
                    // next promotion candidate (next queued writer or, if
                    // none, drain readers).
                    lock (_gate) { _writerHeld = false; }
                    continue;
                }

                // Hand ownership to the batch of readers outside the lock.
                // A canceled async waiter returns false from TrySetResult;
                // sync waiters never cancel, so they always return true.
                int canceled = 0;
                for (int i = 0; i < batchCount; i++)
                {
                    if (!readers[i].TrySetResult()) canceled++;
                }
                if (canceled > 0) lock (_gate) { _readers -= canceled; }
                // Loop: there may be more readers to drain, or a writer to
                // promote once the readers we just woke release.
            }
        }

        public void Dispose()
        {
            Queue<WaiterNode>? readers, writers;
            lock (_gate)
            {
                if (_disposed) return;
                _disposed = true;
                readers = _readerQueue; _readerQueue = null;
                writers = _writerQueue; _writerQueue = null;
            }

            if (readers is not null) foreach (var w in readers) w.SetDisposed();
            if (writers is not null) foreach (var w in writers) w.SetDisposed();
        }

        // ── Waiter node hierarchy ────────────────────────────────────────

        // Polymorphic queue entry: async waiters wrap a TCS; sync waiters
        // wrap a ManualResetEventSlim. Both implement the same TrySetResult
        // / SetDisposed surface so TryPromote and Dispose treat them
        // uniformly.
        private abstract class WaiterNode
        {
            // True for the writer queues, false for the reader queues. Kept
            // for assertions / debugging; the queue the node lives in is the
            // authoritative source.
            public readonly bool IsWriter;
            protected WaiterNode(bool isWriter) { IsWriter = isWriter; }

            /// <summary>
            /// Hand the slot to this waiter. Returns false if the waiter is
            /// already canceled (caller must refund the slot).
            /// </summary>
            public abstract bool TrySetResult();

            /// <summary>Mark the waiter as disposed (failing the wait with
            /// <see cref="ObjectDisposedException"/>).</summary>
            public abstract void SetDisposed();
        }

        private sealed class AsyncWaiter : WaiterNode
        {
            // RunContinuationsAsynchronously so the continuation doesn't
            // stack-dive inside the release thread.
            private readonly TaskCompletionSource _tcs =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public AsyncWaiter(bool isWriter) : base(isWriter) { }

            public override bool TrySetResult() => _tcs.TrySetResult();
            public override void SetDisposed() =>
                _tcs.TrySetException(new ObjectDisposedException(nameof(AsyncReaderWriterLock)));

            public ValueTask AwaitWithCancellation(CancellationToken ct)
            {
                if (!ct.CanBeCanceled) return new ValueTask(_tcs.Task);
                var reg = ct.Register(static s => ((TaskCompletionSource)s!).TrySetCanceled(), _tcs);
                return AwaitWithRegistrationAsync(_tcs, reg);
            }

            private static async ValueTask AwaitWithRegistrationAsync(TaskCompletionSource tcs, CancellationTokenRegistration reg)
            {
                try { await tcs.Task.ConfigureAwait(false); }
                finally { reg.Dispose(); }
            }
        }

        private sealed class SyncWaiter : WaiterNode
        {
            // Caller's thread parks here until the release path signals.
            // We dispose the event right after the wait returns — the
            // waiter is single-shot.
            private readonly ManualResetEventSlim _event = new(initialState: false);
            private volatile bool _disposedRequested;

            public SyncWaiter(bool isWriter) : base(isWriter) { }

            public override bool TrySetResult()
            {
                _event.Set();
                return true; // sync waiters can't be canceled
            }

            public override void SetDisposed()
            {
                _disposedRequested = true;
                _event.Set();
            }

            // Blocks the calling thread until the slot is handed to us.
            // Throws ObjectDisposedException if the lock was disposed while
            // we were queued.
            public void Wait()
            {
                try
                {
                    _event.Wait();
                    if (_disposedRequested)
                        throw new ObjectDisposedException(nameof(AsyncReaderWriterLock));
                }
                finally
                {
                    _event.Dispose();
                }
            }
        }

        // 16-slot stack-allocated buffer used by TryPromote to drain queued
        // readers in batches. stackalloc WaiterNode[16] can't be used
        // because WaiterNode is a reference type; [InlineArray] gives us
        // the same "fixed-size buffer in a struct" shape that's safe for
        // managed element types.
        [System.Runtime.CompilerServices.InlineArray(Capacity)]
        private struct ReaderBatch
        {
            public const int Capacity = 16;
            private WaiterNode _slot;
        }
    }
}
