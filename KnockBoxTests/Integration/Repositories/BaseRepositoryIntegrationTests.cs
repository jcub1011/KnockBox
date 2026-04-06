using KnockBox.Data.DbContexts;
using KnockBox.Data.Entities.Testing;
using KnockBox.Data.Services.KeyProviders.TestEntities;
using KnockBox.Data.Services.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace KnockBox.Tests.Integration.Repositories;

[DoNotParallelize]
[TestClass]
public sealed class BaseRepositoryIntegrationTests
{
    private const string ConnectionStringName = "DefaultConnection";
    private string _connectionString = string.Empty;
    private PostgresContextFactory _factory = null!;
    private BaseRepository<TestEntity> _repo = null!;

    [TestInitialize]
    public async Task Initialize()
    {
        _connectionString = ResolveConnectionString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            Assert.Inconclusive($"Missing connection string '{ConnectionStringName}' in user secrets.");
        }

        _factory = new PostgresContextFactory(_connectionString);
        _repo = new BaseRepository<TestEntity>(_factory, new TestEntityKeyProvider());

        await using var ctx = await _factory.CreateDbContextAsync();
        await TruncateTestEntityAsync(ctx);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (_factory is null) return;
        await using var ctx = await _factory.CreateDbContextAsync();
        await TruncateTestEntityAsync(ctx);
    }

    [TestMethod]
    public async Task CreateAsync_PersistsEntity()
    {
        var model = new TestEntity { TestData = "inte-create", TestDate = DateTime.UtcNow };

        await _repo.CreateAsync(model, CancellationToken.None);

        await using var ctx = await _factory.CreateDbContextAsync();
        var stored = await ctx.TestEntity.SingleAsync();
        Assert.AreEqual("inte-create", stored.TestData);
    }

    [TestMethod]
    public async Task UpdateAsync_PersistsChanges()
    {
        var model = new TestEntity { TestData = "inte-update", TestDate = DateTime.UtcNow };
        await SeedAsync(model);

        model.TestData = "inte-updated";
        await _repo.UpdateAsync(model, CancellationToken.None);

        await using var ctx = await _factory.CreateDbContextAsync();
        var stored = await ctx.TestEntity.SingleAsync();
        Assert.AreEqual("inte-updated", stored.TestData);
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesEntity()
    {
        var model = new TestEntity { TestData = "inte-delete", TestDate = DateTime.UtcNow };
        await SeedAsync(model);

        await _repo.DeleteAsync(model, CancellationToken.None);

        await using var ctx = await _factory.CreateDbContextAsync();
        Assert.AreEqual(0, await ctx.TestEntity.CountAsync());
    }

    [TestMethod]
    public async Task ExecuteInTransaction_Exception_RollsBack()
    {
        var model = new TestEntity { TestData = "inte-rollback", TestDate = DateTime.UtcNow };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _repo.ExecuteInTransaction(async op =>
            {
                await _repo.CreateAsync(op, model, CancellationToken.None);
                throw new InvalidOperationException("boom");
            }, CancellationToken.None);
        });

        await using var ctx = await _factory.CreateDbContextAsync();
        Assert.AreEqual(0, await ctx.TestEntity.CountAsync());
    }

    [TestMethod]
    public async Task ExecuteInContext_PersistsChanges()
    {
        var model = new TestEntity { TestData = "inte-context", TestDate = DateTime.UtcNow };

        await _repo.ExecuteInContext(async op =>
        {
            await _repo.CreateAsync(op, model, CancellationToken.None);
        }, CancellationToken.None);

        await using var ctx = await _factory.CreateDbContextAsync();
        Assert.AreEqual(1, await ctx.TestEntity.CountAsync());
    }

    [TestMethod]
    public async Task ExecuteInContext_ReturnsValue()
    {
        var result = await _repo.ExecuteInContext(op => Task.FromResult(42), CancellationToken.None);
        Assert.AreEqual(42, result);
    }

    [TestMethod]
    public async Task ExecuteInContext_Exception_DoesNotPersistChanges()
    {
        var model = new TestEntity { TestData = "inte-context-fail", TestDate = DateTime.UtcNow };

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await _repo.ExecuteInContext(async op =>
            {
                await _repo.CreateAsync(op, model, CancellationToken.None);
                throw new InvalidOperationException("boom");
            }, CancellationToken.None);
        });

        await using var ctx = await _factory.CreateDbContextAsync();
        Assert.AreEqual(0, await ctx.TestEntity.CountAsync());
    }

    private static string? ResolveConnectionString()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets<BaseRepositoryIntegrationTests>(optional: true)
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        return config.GetConnectionString(ConnectionStringName);
    }

    private static Task TruncateTestEntityAsync(ApplicationDbContext ctx)
    {
        return ctx.Database.ExecuteSqlRawAsync("""TRUNCATE TABLE "public"."TestEntity" RESTART IDENTITY;""");
    }

    private async Task SeedAsync(params TestEntity[] models)
    {
        await using var ctx = await _factory.CreateDbContextAsync();
        ctx.TestEntity.AddRange(models);
        await ctx.SaveChangesAsync();
    }

    private sealed class PostgresContextFactory(string connectionString) : IDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext() =>
            CreateDbContextAsync().GetAwaiter().GetResult();

        public ValueTask<ApplicationDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseNpgsql(connectionString)
                .Options;

            return ValueTask.FromResult(new ApplicationDbContext(options));
        }
    }
}