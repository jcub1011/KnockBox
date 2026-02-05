using KnockBox.Data.DbContexts;
using KnockBox.Data.Entities.Testing;
using KnockBox.Data.Services.KeyProviders.TestEntities;
using KnockBox.Data.Services.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Data.Common;

namespace KnockBox.Tests.Unit.Repositories;

[TestClass]
public sealed class BaseRepositoryTests
{
    #region Create

    [TestMethod]
    public async Task CreateAsync_SingleModel_AddsEntity()
    {
        var (repo, factory) = CreateRepository();
        var model = new TestEntity { TestData = "single", TestDate = DateTime.UtcNow };

        await repo.CreateAsync(model, CancellationToken.None);

        await using var ctx = await factory.CreateDbContextAsync();
        var stored = await ctx.TestEntity.SingleAsync();
        Assert.AreEqual("single", stored.TestData);
    }

    [TestMethod]
    public async Task CreateAsync_Range_AddsEntities()
    {
        var (repo, factory) = CreateRepository();
        var models = new[]
        {
            new TestEntity { TestData = "one", TestDate = DateTime.UtcNow },
            new TestEntity { TestData = "two", TestDate = DateTime.UtcNow }
        };

        await repo.CreateAsync(models, CancellationToken.None);

        await using var ctx = await factory.CreateDbContextAsync();
        var stored = await ctx.TestEntity.OrderBy(m => m.TestData).ToArrayAsync();
        Assert.HasCount(2, stored);
        Assert.AreEqual("one", stored[0].TestData);
        Assert.AreEqual("two", stored[1].TestData);
    }

    [TestMethod]
    public async Task CreateAsync_WithTransaction_AddsEntity()
    {
        var (repo, factory) = CreateRepository();
        var model = new TestEntity { TestData = "txn", TestDate = DateTime.UtcNow };

        await repo.ExecuteInTransaction(async op =>
        {
            await repo.CreateAsync(op, model, CancellationToken.None);
        }, CancellationToken.None);

        await using var ctx = await factory.CreateDbContextAsync();
        Assert.AreEqual(1, await ctx.TestEntity.CountAsync());
    }

    #endregion

    #region Read (GetAsync)

    [TestMethod]
    public async Task GetAsync_ReturnsResultFromQuery()
    {
        var (repo, factory) = CreateRepository();
        await SeedAsync(factory, new TestEntity { TestData = "findme", TestDate = DateTime.UtcNow });

        var result = await repo.GetAsync(q => q.SingleAsync(m => m.TestData == "findme"), CancellationToken.None);

        Assert.AreEqual("findme", result.TestData);
    }

    [TestMethod]
    public async Task GetAsync_WithTransaction_ReturnsResult()
    {
        var (repo, factory) = CreateRepository();
        await SeedAsync(factory, new TestEntity { TestData = "txnFind", TestDate = DateTime.UtcNow });

        var result = await repo.ExecuteInTransaction(async op =>
        {
            return await repo.GetAsync(op, q => q.SingleAsync(m => m.TestData == "txnFind"), CancellationToken.None);
        }, CancellationToken.None);

        Assert.AreEqual("txnFind", result.TestData);
    }

    #endregion

    #region Update

    [TestMethod]
    public async Task UpdateAsync_SingleModel_UpdatesEntity()
    {
        var (repo, factory) = CreateRepository();
        var model = new TestEntity { TestData = "original", TestDate = DateTime.UtcNow };
        await SeedAsync(factory, model);

        // Disconnected update
        model.TestData = "updated";
        await repo.UpdateAsync(model, CancellationToken.None);

        await using var ctx = await factory.CreateDbContextAsync();
        var stored = await ctx.TestEntity.SingleAsync();
        Assert.AreEqual("updated", stored.TestData);
    }

    [TestMethod]
    public async Task UpdateAsync_Range_UpdatesEntities()
    {
        var (repo, factory) = CreateRepository();
        var m1 = new TestEntity { TestData = "orig1", TestDate = DateTime.UtcNow };
        var m2 = new TestEntity { TestData = "orig2", TestDate = DateTime.UtcNow };
        await SeedAsync(factory, m1, m2);

        m1.TestData = "upd1";
        m2.TestData = "upd2";
        await repo.UpdateAsync(new[] { m1, m2 }, CancellationToken.None);

        await using var ctx = await factory.CreateDbContextAsync();
        var stored = await ctx.TestEntity.OrderBy(m => m.TestData).ToArrayAsync();
        Assert.IsTrue(stored.Any(x => x.TestData == "upd1"));
        Assert.IsTrue(stored.Any(x => x.TestData == "upd2"));
    }

    [TestMethod]
    public async Task UpdateAsync_BySingleQuery_UpdatesEntity()
    {
        var (repo, factory) = CreateRepository();
        await SeedAsync(factory, new TestEntity { TestData = "target", TestDate = DateTime.UtcNow });

        await repo.UpdateAsync(
            q => q.SingleAsync(m => m.TestData == "target"),
            m => m.TestData = "hit",
            CancellationToken.None);

        await using var ctx = await factory.CreateDbContextAsync();
        var stored = await ctx.TestEntity.SingleAsync();
        Assert.AreEqual("hit", stored.TestData);
    }

    [TestMethod]
    public async Task UpdateAsync_ByListQuery_UpdatesEntities()
    {
        var (repo, factory) = CreateRepository();
        await SeedAsync(factory,
            new TestEntity { TestData = "group", TestDate = DateTime.UtcNow },
            new TestEntity { TestData = "group", TestDate = DateTime.UtcNow },
            new TestEntity { TestData = "other", TestDate = DateTime.UtcNow });

        await repo.UpdateAsync(
            q => q.Where(m => m.TestData == "group"),
            m => m.TestData = "changed",
            CancellationToken.None);

        await using var ctx = await factory.CreateDbContextAsync();
        var count = await ctx.TestEntity.CountAsync(m => m.TestData == "changed");
        Assert.AreEqual(2, count);
    }

    #endregion

    #region Delete

    [TestMethod]
    public async Task DeleteAsync_SingleModel_RemovesEntity()
    {
        var (repo, factory) = CreateRepository();
        var model = new TestEntity { TestData = "delete-me", TestDate = DateTime.UtcNow };
        await SeedAsync(factory, model);

        await repo.DeleteAsync(model, CancellationToken.None);

        await using var ctx = await factory.CreateDbContextAsync();
        Assert.AreEqual(0, await ctx.TestEntity.CountAsync());
    }

    [TestMethod]
    public async Task DeleteAsync_Range_RemovesEntities()
    {
        var (repo, factory) = CreateRepository();
        var m1 = new TestEntity { TestData = "del1", TestDate = DateTime.UtcNow };
        var m2 = new TestEntity { TestData = "del2", TestDate = DateTime.UtcNow };
        await SeedAsync(factory, m1, m2);

        await repo.DeleteAsync(new[] { m1, m2 }, CancellationToken.None);

        await using var ctx = await factory.CreateDbContextAsync();
        Assert.AreEqual(0, await ctx.TestEntity.CountAsync());
    }

    [TestMethod]
    public async Task DeleteAsync_ByQuery_RemovesEntities()
    {
        var (repo, factory) = CreateRepository();
        await SeedAsync(factory,
            new TestEntity { TestData = "remove", TestDate = DateTime.UtcNow },
            new TestEntity { TestData = "keep", TestDate = DateTime.UtcNow });

        await repo.DeleteAsync(q => q.Where(m => m.TestData == "remove"), CancellationToken.None);

        await using var ctx = await factory.CreateDbContextAsync();
        var remaining = await ctx.TestEntity.SingleAsync();
        Assert.AreEqual("keep", remaining.TestData);
    }

    #endregion

    #region Transaction & Edge Cases

    [TestMethod]
    public async Task ExecuteInTransaction_ReturnsValue()
    {
        var (repo, _) = CreateRepository();
        var result = await repo.ExecuteInTransaction(op => Task.FromResult(42), CancellationToken.None);
        Assert.AreEqual(42, result);
    }

    [TestMethod]
    public async Task ExecuteInTransaction_CallbackException_RollsBackChanges()
    {
        var (repo, factory) = CreateRepository();
        var model = new TestEntity { TestData = "should-rollback", TestDate = DateTime.UtcNow };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await repo.ExecuteInTransaction(async op =>
            {
                await repo.CreateAsync(op, model, CancellationToken.None);
                throw new InvalidOperationException("Boom");
            }, CancellationToken.None);
        });

        await using var ctx = await factory.CreateDbContextAsync();
        Assert.AreEqual(0, await ctx.TestEntity.CountAsync(), "Entity should not be committed.");
    }

    [TestMethod]
    public async Task CreateAsync_WithCanceledToken_ThrowsOperationCanceledException()
    {
        var (repo, _) = CreateRepository();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            repo.CreateAsync(new TestEntity(), cts.Token));
    }

    #endregion

    #region Helpers

    private static async Task SeedAsync(InMemoryContextFactory factory, params TestEntity[] models)
    {
        await using var ctx = await factory.CreateDbContextAsync();
        ctx.TestEntity.AddRange(models);
        await ctx.SaveChangesAsync();
    }

    private static (BaseRepository<TestEntity> Repository, InMemoryContextFactory Factory) CreateRepository()
    {
        var factory = new InMemoryContextFactory();
        var repo = new BaseRepository<TestEntity>(factory, new TestEntityKeyProvider());
        return (repo, factory);
    }

    private sealed class InMemoryContextFactory : IDbContextFactory<ApplicationDbContext>, IDisposable
    {
        private readonly DbConnection _connection;

        public InMemoryContextFactory()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            var options = CreateOptions();
            using var ctx = new ApplicationDbContext(options);
            ctx.Database.EnsureCreated();
        }

        public ApplicationDbContext CreateDbContext()
        {
            return CreateDbContextAsync().GetAwaiter().GetResult();
        }

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(new ApplicationDbContext(CreateOptions()));
        }

        public void Dispose()
        {
            _connection.Dispose();
        }

        private DbContextOptions<ApplicationDbContext> CreateOptions()
        {
            return new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(_connection)
                .Options;
        }
    }

    #endregion
}