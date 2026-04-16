using KnockBox.Core.Extensions.ThreadSafety;

namespace KnockBox.Tests.Unit.Extensions.ThreadSafety;

[TestClass]
public sealed class ReaderWriterLockSlimExtensionsTests
{
    // ── EnterReadScope ───────────────────────────────────────────────────────

    [TestMethod]
    public void EnterReadScope_AcquiresReadLock()
    {
        var rwLock = new ReaderWriterLockSlim();

        using var scope = rwLock.EnterReadScope();

        Assert.IsTrue(scope.Valid);
        Assert.IsTrue(scope.Permissions.HasFlag(LockPermissions.Read));
        Assert.IsFalse(scope.Permissions.HasFlag(LockPermissions.Write));
        Assert.IsTrue(rwLock.IsReadLockHeld);
    }

    [TestMethod]
    public void EnterReadScope_Dispose_ReleasesReadLock()
    {
        var rwLock = new ReaderWriterLockSlim();

        var scope = rwLock.EnterReadScope();
        scope.Dispose();

        Assert.IsFalse(rwLock.IsReadLockHeld);
    }

    // ── EnterWriteScope ──────────────────────────────────────────────────────

    [TestMethod]
    public void EnterWriteScope_AcquiresWriteLock()
    {
        var rwLock = new ReaderWriterLockSlim();

        using var scope = rwLock.EnterWriteScope();

        Assert.IsTrue(scope.Valid);
        Assert.IsTrue(scope.Permissions.HasFlag(LockPermissions.Read));
        Assert.IsTrue(scope.Permissions.HasFlag(LockPermissions.Write));
        Assert.IsTrue(rwLock.IsWriteLockHeld);
    }

    [TestMethod]
    public void EnterWriteScope_Dispose_ReleasesWriteLock()
    {
        var rwLock = new ReaderWriterLockSlim();

        var scope = rwLock.EnterWriteScope();
        scope.Dispose();

        Assert.IsFalse(rwLock.IsWriteLockHeld);
    }

    // ── Read ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public void Read_ReturnsValue_AndReleasesLock()
    {
        var rwLock = new ReaderWriterLockSlim();
        var value = 42;

        var read = rwLock.Read(in value);

        Assert.AreEqual(42, read);
        Assert.IsFalse(rwLock.IsReadLockHeld);
    }

    // ── Exchange ─────────────────────────────────────────────────────────────

    [TestMethod]
    public void Exchange_ReplacesValue_ReturnsOldValue()
    {
        var rwLock = new ReaderWriterLockSlim();
        var value = 10;

        var old = rwLock.Exchange(ref value, 20);

        Assert.AreEqual(10, old);
        Assert.AreEqual(20, value);
    }

    [TestMethod]
    public void Exchange_ReleasesWriteLockAfterExchange()
    {
        var rwLock = new ReaderWriterLockSlim();
        var value = 0;

        rwLock.Exchange(ref value, 1);

        Assert.IsFalse(rwLock.IsWriteLockHeld);
        Assert.IsFalse(rwLock.IsReadLockHeld);
    }

    // ── CompareExchange with callback ─────────────────────────────────────────

    [TestMethod]
    public void CompareExchange_Callback_ConditionTrue_ReplacesValue()
    {
        var rwLock = new ReaderWriterLockSlim();
        var value = 5;

        var old = rwLock.CompareExchange(ref value, 99, v => v == 5);

        Assert.AreEqual(5, old);
        Assert.AreEqual(99, value);
    }

    [TestMethod]
    public void CompareExchange_Callback_ConditionFalse_LeavesValueUnchanged()
    {
        var rwLock = new ReaderWriterLockSlim();
        var value = 5;

        var old = rwLock.CompareExchange(ref value, 99, v => v == 100);

        Assert.AreEqual(5, old);
        Assert.AreEqual(5, value);
    }

    [TestMethod]
    public void CompareExchange_Callback_ReleasesWriteLock()
    {
        var rwLock = new ReaderWriterLockSlim();
        var value = 0;

        rwLock.CompareExchange(ref value, 1, _ => true);

        Assert.IsFalse(rwLock.IsWriteLockHeld);
        Assert.IsFalse(rwLock.IsReadLockHeld);
    }

    // ── CompareExchange with comparand ────────────────────────────────────────

    [TestMethod]
    public void CompareExchange_Comparand_Matches_ReplacesValue()
    {
        var rwLock = new ReaderWriterLockSlim();
        var value = 5;

        var old = rwLock.CompareExchange(ref value, 99, 5);

        Assert.AreEqual(5, old);
        Assert.AreEqual(99, value);
    }

    [TestMethod]
    public void CompareExchange_Comparand_NoMatch_LeavesValueUnchanged()
    {
        var rwLock = new ReaderWriterLockSlim();
        var value = 5;

        var old = rwLock.CompareExchange(ref value, 99, 42);

        Assert.AreEqual(5, old);
        Assert.AreEqual(5, value);
    }

    [TestMethod]
    public void CompareExchange_Comparand_ReleasesWriteLock()
    {
        var rwLock = new ReaderWriterLockSlim();
        var value = 0;

        rwLock.CompareExchange(ref value, 1, 0);

        Assert.IsFalse(rwLock.IsWriteLockHeld);
        Assert.IsFalse(rwLock.IsReadLockHeld);
    }
}
