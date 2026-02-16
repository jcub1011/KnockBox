using KnockBox.Extensions.ThreadSafety;
using System.Collections;

namespace KnockBox.Extensions.Collections
{
    public class ThreadSafeList<TElement> : IDisposable, IList<TElement>
    {
        private bool _disposed;
        private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
        private readonly List<TElement> _list = [];

        public ReadLockScope EnterReadLockScope() => new(_lock);
        public WriteLockScope EnterWriteLockScope() => new(_lock);

        public int Count => GetCount();

        public bool IsReadOnly => false;

        public TElement this[int index]
        {
            get
            {
                return At(index);
            }
            set
            {
                using var scope = EnterWriteLockScope();
                _list[index] = value;
            }
        }

        public int GetCount()
        {
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
            return GetEnumerator();
        }
    }
}
