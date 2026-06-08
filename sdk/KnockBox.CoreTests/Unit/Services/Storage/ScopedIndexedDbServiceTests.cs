using KnockBox.Core.Primitives.Returns;
using KnockBox.Core.Services.Storage.IndexedDb;
using Moq;

namespace KnockBox.Tests.Unit.Services.Storage;

[TestClass]
public sealed class ScopedIndexedDbServiceTests
{
    [TestMethod]
    public async Task OpenAsync_PrefixesDatabaseName_PreservingOtherSchemaFields()
    {
        var inner = new Mock<IIndexedDbService>(MockBehavior.Strict);
        IndexedDbSchema? captured = null;
        inner.Setup(s => s.OpenAsync(It.IsAny<IndexedDbSchema>(), It.IsAny<CancellationToken>()))
            .Callback<IndexedDbSchema, CancellationToken>((s, _) => captured = s)
            .Returns(new ValueTask<ValueResult<IIndexedDatabase, IndexedDbError>>(
                ValueResult<IIndexedDatabase, IndexedDbError>.Canceled));

        var scoped = new ScopedIndexedDbService(inner.Object, "dnd-mapper");
        var schema = new IndexedDbSchema("Library", 4)
        {
            Stores = [new DeclaredStore("maps", DeclaredStoreKind.Json)],
        };

        await scoped.OpenAsync(schema);

        Assert.IsNotNull(captured);
        Assert.AreEqual("dnd-mapper::Library", captured!.Name);
        Assert.AreEqual(4, captured.Version);
        Assert.IsNotNull(captured.Stores);
        Assert.HasCount(1, captured.Stores!);
    }

    [TestMethod]
    public async Task DeleteDatabaseAsync_PrefixesName()
    {
        var inner = new Mock<IIndexedDbService>(MockBehavior.Strict);
        inner.Setup(s => s.DeleteDatabaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));

        var scoped = new ScopedIndexedDbService(inner.Object, "dnd-mapper");
        await scoped.DeleteDatabaseAsync("Library");

        inner.Verify(s => s.DeleteDatabaseAsync("dnd-mapper::Library", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ListDatabasesAsync_ReturnsOnlyOwnDatabases_WithPrefixStripped()
    {
        var inner = new Mock<IIndexedDbService>(MockBehavior.Strict);
        IReadOnlyList<DatabaseInfo> all =
        [
            new("dnd-mapper::Library", 4),
            new("other-plugin::Cache", 2),
            new("UnscopedLegacy", 1),
        ];
        inner.Setup(s => s.ListDatabasesAsync(It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<ValueResult<IReadOnlyList<DatabaseInfo>, IndexedDbError>>(
                ValueResult<IReadOnlyList<DatabaseInfo>, IndexedDbError>.FromValue(all)));

        var scoped = new ScopedIndexedDbService(inner.Object, "dnd-mapper");
        var result = await scoped.ListDatabasesAsync();

        Assert.IsTrue(result.TryGetSuccess(out var infos));
        Assert.HasCount(1, infos);
        Assert.AreEqual("Library", infos[0].Name);
        Assert.AreEqual(4, infos[0].Version);
    }

    [TestMethod]
    public async Task MigrateLegacyDatabaseAsync_PassesLegacySourceVerbatim_PrefixesDestination()
    {
        var inner = new Mock<IIndexedDbService>(MockBehavior.Strict);
        inner.Setup(s => s.MigrateDatabaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));

        var scoped = new ScopedIndexedDbService(inner.Object, "dnd-mapper");
        await scoped.MigrateLegacyDatabaseAsync("KnockBox.DndMapper", new IndexedDbSchema("KnockBox.DndMapper", 4));

        inner.Verify(s => s.MigrateDatabaseAsync(
            "KnockBox.DndMapper", "dnd-mapper::KnockBox.DndMapper", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task MigrateDatabaseAsync_PrefixesBothNames()
    {
        var inner = new Mock<IIndexedDbService>(MockBehavior.Strict);
        inner.Setup(s => s.MigrateDatabaseAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(new ValueTask<Result<IndexedDbError>>(Result<IndexedDbError>.Success));

        var scoped = new ScopedIndexedDbService(inner.Object, "dnd-mapper");
        await scoped.MigrateDatabaseAsync("Old", "New");

        inner.Verify(s => s.MigrateDatabaseAsync(
            "dnd-mapper::Old", "dnd-mapper::New", It.IsAny<CancellationToken>()), Times.Once);
    }
}
