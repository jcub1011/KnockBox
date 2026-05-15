using KnockBox.Core.Services.Storage.IndexedDb;
using Microsoft.JSInterop;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

/// <summary>
/// JS-invokable bridge that owns both ends of the per-database callback contract:
/// <list type="bullet">
/// <item><c>OnUpgrade</c> — fires inside the JS <c>onupgradeneeded</c> handler.
/// Runs the user's <see cref="IndexedDbSchema.OnUpgrade"/> delegate, then flushes
/// queued schema ops back to JS.</item>
/// <item><c>OnVersionChange</c> — fires when another connection (typically a
/// different tab) requests an upgrade and we must close to let it proceed.
/// Raised on the owning <see cref="IndexedDatabase"/>.</item>
/// </list>
/// </summary>
internal sealed class VersionChangeBridge
{
    private readonly IndexedDbInterop _interop;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<VersionChangeBridge> _logger;
    private readonly IndexedDbSchema _schema;
    private IndexedDatabase? _database;

    public VersionChangeBridge(
        IndexedDbInterop interop,
        ILoggerFactory loggerFactory,
        IndexedDbSchema schema)
    {
        _interop = interop;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<VersionChangeBridge>();
        _schema = schema;
    }

    internal void AttachDatabase(IndexedDatabase database) => _database = database;

    /// <summary>
    /// Called from <c>onupgradeneeded</c>. Runs the user's
    /// <see cref="IndexedDbSchema.OnUpgrade"/> delegate and returns the queued
    /// schema ops so JS can apply them synchronously inside the still-active
    /// versionchange transaction.
    /// </summary>
    [JSInvokable]
    public async Task<SchemaOp[]> OnUpgrade(
        int upgradeTxId,
        int oldVersion,
        int newVersion,
        Dictionary<string, string[]> existingSchema)
    {
        if (_schema.OnUpgrade is null)
        {
            throw new InvalidOperationException(
                $"Database '{_schema.Name}' requires an upgrade from v{oldVersion} to v{newVersion} " +
                "but no OnUpgrade handler was provided on the schema.");
        }

        var existing = existingSchema.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<string>)kv.Value);
        var ctx = new UpgradeContext(
            _interop, _loggerFactory, upgradeTxId, oldVersion, newVersion,
            _schema.JsonOptions ?? IndexedDbWireFormat.DefaultJsonOptions,
            existing);

        try
        {
            await _schema.OnUpgrade(ctx, oldVersion, newVersion, CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "User OnUpgrade handler for database '{DatabaseName}' (v{Old} -> v{New}) threw; upgrade will abort.",
                _schema.Name, oldVersion, newVersion);
            ctx.Deactivate();
            throw;
        }

        ctx.Deactivate();
        return ctx.DrainPending();
    }

    [JSInvokable]
    public async Task OnVersionChange()
    {
        if (_database is { } db)
        {
            await db.RaiseVersionChangeRequestedAsync().ConfigureAwait(false);
        }
    }
}
