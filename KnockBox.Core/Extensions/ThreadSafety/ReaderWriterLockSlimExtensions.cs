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

        /// <summary>
        /// Reads the current value at the location.
        /// </summary>
        /// <typeparam name="TType"></typeparam>
        /// <param name="rwLock"></param>
        /// <param name="location"></param>
        /// <returns></returns>
        public static TType Read<TType>(this ReaderWriterLockSlim rwLock, in TType location)
        {
            try
            {
                rwLock.EnterReadLock();
                return location;
            }
            finally
            {
                rwLock.ExitReadLock();
            }
        }

        /// <summary>
        /// Replaces the old value at the destination with the provided value, returning the old value.
        /// </summary>
        /// <typeparam name="TType"></typeparam>
        /// <param name="rwLock"></param>
        /// <param name="destination"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static TType Exchange<TType>(this ReaderWriterLockSlim rwLock, ref TType destination, TType value)
        {
            try
            {
                rwLock.EnterWriteLock();
                var oldValue = destination;
                destination = value;
                return oldValue;
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Replaces the old value at the destination with the provided value if the callback returns true, returning the old value.
        /// </summary>
        /// <typeparam name="TType"></typeparam>
        /// <param name="rwLock"></param>
        /// <param name="destination"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static TType CompareExchange<TType>(this ReaderWriterLockSlim rwLock, ref TType destination, TType value, Func<TType, bool> callback)
        {
            try
            {
                rwLock.EnterWriteLock();
                var oldValue = destination;
                if (callback(oldValue)) destination = value;
                return oldValue;
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Replaces the old value at the destination with the provided value if the old value matches the comparand, returning the old value.
        /// </summary>
        /// <typeparam name="TType"></typeparam>
        /// <param name="rwLock"></param>
        /// <param name="destination"></param>
        /// <param name="value"></param>
        /// <param name="comparand"></param>
        /// <returns></returns>
        public static TType CompareExchange<TType>(this ReaderWriterLockSlim rwLock, ref TType destination, TType value, TType comparand)
            where TType : struct
        {
            try
            {
                rwLock.EnterWriteLock();
                var oldValue = destination;
                if (oldValue.Equals(comparand)) destination = value;
                return oldValue;
            }
            finally
            {
                rwLock.ExitWriteLock();
            }
        }
    }
}
