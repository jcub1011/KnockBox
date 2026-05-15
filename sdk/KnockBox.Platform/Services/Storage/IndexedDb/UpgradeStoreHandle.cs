using KnockBox.Core.Services.Storage.IndexedDb;

namespace KnockBox.Platform.Services.Storage.IndexedDb;

internal sealed class UpgradeStoreHandle : IUpgradeStoreHandle
{
    private readonly UpgradeContext _ctx;
    private readonly List<string> _indexNames;

    public string Name { get; }
    public IReadOnlyList<string> IndexNames => _indexNames;

    public UpgradeStoreHandle(UpgradeContext ctx, string name, List<string> indexNames)
    {
        _ctx = ctx;
        Name = name;
        _indexNames = indexNames;
    }

    public void CreateIndex(string name, KeyPath keyPath, bool unique = false, bool multiEntry = false)
    {
        if (_indexNames.Contains(name))
            throw new InvalidOperationException($"Index '{name}' already exists on store '{Name}'.");

        _ctx.Queue(new SchemaOp(
            Type: "createIndex",
            Name: name,
            StoreName: Name,
            KeyPath: keyPath.Paths.ToArray(),
            Unique: unique ? true : null,
            MultiEntry: multiEntry ? true : null));
        _indexNames.Add(name);
    }

    public void DeleteIndex(string name)
    {
        if (!_indexNames.Remove(name))
            throw new InvalidOperationException($"Index '{name}' does not exist on store '{Name}'.");
        _ctx.Queue(new SchemaOp(Type: "deleteIndex", Name: name, StoreName: Name));
    }
}
