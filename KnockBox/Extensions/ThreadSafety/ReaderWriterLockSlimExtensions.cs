namespace KnockBox.Extensions.ThreadSafety
{
    public static class ReaderWriterLockSlimExtensions
    {
        public static ReadLockScope EnterReadScope(this ReaderWriterLockSlim rwLock)
        {
            return new ReadLockScope(rwLock);
        }

        public static WriteLockScope EnterWriteScope(this ReaderWriterLockSlim rwLock)
        {
            return new WriteLockScope(rwLock);
        }
    }
}
