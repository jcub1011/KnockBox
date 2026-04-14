namespace KnockBox.Core.Extensions.ThreadSafety
{
    [Flags]
    public enum LockPermissions
    {
        Read = 1,
        Write = 2,
    }

    /// <summary>
    /// An object that automatically aquires and releases the lock for <see cref="ReaderWriterLockSlim"/>.
    /// </summary>
    public interface IRWLockScope : IDisposable
    {
        /// <summary>
        /// If this thread has the lock from the scope.
        /// </summary>
        public bool Valid { get; }

        /// <summary>
        /// The permissions this scope has.
        /// </summary>
        public LockPermissions Permissions { get; }
    }

    /// <summary>
    /// A lock scope with read only permissions.
    /// </summary>
    public sealed class ReadLockScope : IRWLockScope
    {
        private readonly ReaderWriterLockSlim _lock;
        public bool Valid => _lock.IsReadLockHeld;
        public LockPermissions Permissions => LockPermissions.Read;

        public ReadLockScope(ReaderWriterLockSlim rwLock)
        {
            _lock = rwLock;
            _lock.EnterReadLock();
        }

        public void Dispose()
        {
            if (Valid) _lock.ExitReadLock();
        }
    }

    /// <summary>
    /// A lock scope with read and write permissions.
    /// </summary>
    public sealed class WriteLockScope : IRWLockScope
    {
        private readonly ReaderWriterLockSlim _lock;
        public bool Valid => _lock.IsWriteLockHeld;
        public LockPermissions Permissions => LockPermissions.Read | LockPermissions.Write;

        public WriteLockScope(ReaderWriterLockSlim rwLock)
        {
            _lock = rwLock;
            _lock.EnterWriteLock();
        }

        public void Dispose()
        {
            if (Valid) _lock.ExitWriteLock();
        }
    }
}
