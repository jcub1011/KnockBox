using Microsoft.JSInterop;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

/// <summary>
/// JS-invokable bridge that forwards <c>onversionchange</c> events from the
/// JS-side IDBDatabase to the owning <see cref="IndexedDatabase"/>. The
/// JS-side fires this when another connection (typically a different tab)
/// requests an upgrade and the open connection must close to let it proceed.
/// </summary>
internal sealed class VersionChangeBridge
{
    private readonly ILogger<VersionChangeBridge> _logger;
    private IndexedDatabase? _database;

    public VersionChangeBridge(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<VersionChangeBridge>();
    }

    internal void AttachDatabase(IndexedDatabase database) => _database = database;

    [JSInvokable]
    public async Task OnVersionChange()
    {
        if (_database is { } db)
        {
            try
            {
                await db.RaiseVersionChangeRequestedAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "VersionChangeBridge.OnVersionChange threw.");
            }
        }
    }
}
