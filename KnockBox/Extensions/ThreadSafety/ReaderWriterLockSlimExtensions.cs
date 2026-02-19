using System.Runtime.CompilerServices;

namespace KnockBox.Extensions.ThreadSafety
{
    public static class ReaderWriterLockSlimExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ReadLockScope EnterReadScope(this ReaderWriterLockSlim rwLock)
        {
            return new ReadLockScope(rwLock);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static WriteLockScope EnterWriteScope(this ReaderWriterLockSlim rwLock)
        {
            return new WriteLockScope(rwLock);
        }
    }
}
