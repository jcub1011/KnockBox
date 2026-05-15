namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// Mode of an IndexedDB transaction. <see cref="ReadOnly"/> permits concurrent
    /// transactions over the same store; <see cref="ReadWrite"/> serializes them.
    /// </summary>
    public enum TransactionMode
    {
        ReadOnly = 0,
        ReadWrite = 1,
    }
}
