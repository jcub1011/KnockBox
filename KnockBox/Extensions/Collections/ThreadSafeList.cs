using KnockBox.Extensions.ThreadSafety;
using System.Collections;

namespace KnockBox.Extensions.Collections
{
    public class ThreadSafeList<TElement> : IDisposable, IEnumerable<TElement>
    {
        private bool _disposed;
        private readonly ReaderWriterLockSlim _lock = new();
        private readonly List<TElement> _list = [];

        public ReadLockScope GetReadLockScope() => new(_lock);
        public WriteLockScope GetWriteLockScope() => new(_lock);

        public int GetCount(RWLockScope scope)
        {
            AssertLockHeld(scope);
            return _list.Count;
        }

        public bool IsReadOnly => false;

        public TElement At(int index, RWLockScope scope)
        {
            AssertLockHeld(scope);
            return _list[index];
        }

        public void Dispose()
        {
            if (_disposed) return;
            GC.SuppressFinalize(this);
        }

        public int IndexOf(TElement item)
        {
            return ((IList<TElement>)_list).IndexOf(item);
        }

        public void Insert(int index, TElement item)
        {
            ((IList<TElement>)_list).Insert(index, item);
        }

        public void RemoveAt(int index)
        {
            ((IList<TElement>)_list).RemoveAt(index);
        }

        public void Add(TElement item)
        {
            ((ICollection<TElement>)_list).Add(item);
        }

        public void Clear()
        {
            ((ICollection<TElement>)_list).Clear();
        }

        public bool Contains(TElement item)
        {
            return ((ICollection<TElement>)_list).Contains(item);
        }

        public void CopyTo(TElement[] array, int arrayIndex)
        {
            ((ICollection<TElement>)_list).CopyTo(array, arrayIndex);
        }

        public bool Remove(TElement item)
        {
            return ((ICollection<TElement>)_list).Remove(item);
        }

        public IEnumerator<TElement> GetEnumerator()
        {
            return ((IEnumerable<TElement>)_list).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable)_list).GetEnumerator();
        }

        private static void AssertLockHeld(RWLockScope scope)
        {
            if (!scope.Valid) throw new InvalidOperationException("Scope no longer has lock.");
        }
    }
}
