namespace KnockBox.Extensions.ThreadSafety
{
    public abstract class RWLockScope : IDisposable
    {
        public abstract bool Valid { get; }
        protected readonly ReaderWriterLockSlim Lock;

        public RWLockScope(ReaderWriterLockSlim rwLock)
        {
            Lock = rwLock;
            EnterLock();
        }

        public void Dispose()
        {
            if (!Valid) return;
            ExitLock();
            GC.SuppressFinalize(this);
        }

        protected abstract void EnterLock();
        protected abstract void ExitLock();
    }

    public sealed class ReadLockScope(ReaderWriterLockSlim rwLock) : RWLockScope(rwLock)
    {
        public override bool Valid => Lock.IsReadLockHeld;

        protected override void EnterLock()
        {
            Lock.EnterReadLock();
        }

        protected override void ExitLock()
        {
            Lock.ExitReadLock();
        }
    }

    public sealed class WriteLockScope(ReaderWriterLockSlim rwLock) : RWLockScope(rwLock)
    {
        public override bool Valid => Lock.IsWriteLockHeld;

        protected override void EnterLock()
        {
            Lock.EnterWriteLock();
        }

        protected override void ExitLock()
        {
            Lock.ExitWriteLock();
        }
    }
}
