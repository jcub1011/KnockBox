using KnockBox.Core.Extensions.ThreadSafety;
using System.Collections;

namespace KnockBox.Core.Extensions.Collections
{
    /// <summary>
    /// A <see cref="List{T}"/> guarded by a <see cref="ReaderWriterLockSlim"/>.
    /// All standard <see cref="IList{T}"/> operations are individually
    /// thread-safe; the scope-taking overloads let callers compose multi-step
    /// operations (e.g., "find then insert") under a single held lock.
    /// </summary>
    /// <remarks>
    /// Most per-room state does not need this — the room's Execute lock on
    /// <see cref="Games.Shared.AbstractGameState"/> already serializes access.
    /// Reach for <see cref="ThreadSafeList{TElement}"/> when a collection is
    /// shared across states (history buffers, leaderboards) or read by
    /// background services that run outside any Execute block.
    /// </remarks>
    public class ThreadSafeList<TElement> : IDisposable, IList<TElement>
    {
        private bool _disposed;
        private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
        private readonly List<TElement> _list = [];
        public bool IsDisposed => _disposed;

        public ReadLockScope EnterReadLockScope()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new(_lock);
        }

        public WriteLockScope EnterWriteLockScope()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new(_lock);
        }

        public int Count => GetCount();

        public bool IsReadOnly => false;

        public TElement this[int index]
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return At(index);
            }
            set
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                using var scope = EnterWriteLockScope();
                _list[index] = value;
            }
        }

        public int GetCount()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var scope = EnterReadLockScope();
            return GetCount(scope);
        }

        public int GetCount(IRWLockScope scope)
        {
            AssertReadLockHeld(scope);
            return _list.Count;
        }

        public TElement At(int index)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var scope = EnterReadLockScope();
            return At(index, scope);
        }

        public TElement At(int index, IRWLockScope scope)
        {
            AssertReadLockHeld(scope);
            return _list[index];
        }

        public int IndexOf(TElement item)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var scope = EnterReadLockScope();
            return IndexOf(item, scope);
        }

        public int IndexOf(TElement item, IRWLockScope scope)
        {
            AssertReadLockHeld(scope);
            return _list.IndexOf(item);
        }

        public void Insert(int index, TElement item)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var scope = EnterWriteLockScope();
            Insert(index, item, scope);
        }

        public void Insert(int index, TElement item, IRWLockScope scope)
        {
            AssertWriteLockHeld(scope);
            _list.Insert(index, item);
        }

        public void RemoveAt(int index)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var scope = EnterWriteLockScope();
            RemoveAt(index, scope);
        }

        public void RemoveAt(int index, IRWLockScope scope)
        {
            AssertWriteLockHeld(scope);
            _list.RemoveAt(index);
        }

        public void Add(TElement item)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var scope = EnterWriteLockScope();
            Add(item, scope);
        }

        public void Add(TElement item, IRWLockScope scope)
        {
            AssertWriteLockHeld(scope);
            _list.Add(item);
        }

        public void Clear()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var scope = EnterWriteLockScope();
            Clear(scope);
        }

        public void Clear(IRWLockScope scope)
        {
            AssertWriteLockHeld(scope);
            _list.Clear();
        }

        public bool Contains(TElement item)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var scope = EnterReadLockScope();
            return Contains(item, scope);
        }

        public bool Contains(TElement item, IRWLockScope scope)
        {
            AssertReadLockHeld(scope);
            return _list.Contains(item);
        }

        public void CopyTo(TElement[] array, int arrayIndex)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var scope = EnterReadLockScope();
            CopyTo(array, arrayIndex, scope);
        }

        public void CopyTo(TElement[] array, int arrayIndex, IRWLockScope scope)
        {
            AssertReadLockHeld(scope);
            _list.CopyTo(array, arrayIndex);
        }

        public bool Remove(TElement item)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var scope = EnterWriteLockScope();
            return Remove(item, scope);
        }

        public bool Remove(TElement item, IRWLockScope scope)
        {
            AssertWriteLockHeld(scope);
            return _list.Remove(item);
        }

        public IEnumerator<TElement> GetEnumerator()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            using var scope = EnterReadLockScope();
            return GetEnumerator(scope);
        }

        public IEnumerator<TElement> GetEnumerator(IRWLockScope scope)
        {
            AssertReadLockHeld(scope);
            return ((IEnumerable<TElement>)[.. _list]).GetEnumerator();
        }

        public void Dispose()
        {
            if (_disposed) return;

            using (var scope = _lock.EnterWriteScope())
            {
                _list.Clear();
            }

            _lock.Dispose();
            _disposed = true;

            GC.SuppressFinalize(this);
        }

        private void AssertReadLockHeld(IRWLockScope scope)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!scope.Valid) throw new InvalidOperationException("Scope no longer has lock.");
            if (!scope.Permissions.HasFlag(LockPermissions.Read))
                throw new InvalidOperationException("Scope does not have read permissions.");
        }

        private void AssertWriteLockHeld(IRWLockScope scope)
        {
            AssertReadLockHeld(scope);
            if (!scope.Permissions.HasFlag(LockPermissions.Write))
                throw new InvalidOperationException("Scope does not write permissions.");
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return GetEnumerator();
        }
    }
}
