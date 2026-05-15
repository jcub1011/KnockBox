namespace KnockBox.Core.Services.Storage.IndexedDb
{
    /// <summary>
    /// Metadata for a database known to the current origin.
    /// </summary>
    public readonly record struct DatabaseInfo(string Name, int Version);
}
