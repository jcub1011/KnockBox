
using KnockBox.Data.DbContexts;
using KnockBox.Extensions;
using Microsoft.EntityFrameworkCore;

namespace KnockBox.Data.Services.Repositories
{
    public abstract class AbstractRepository<TModel>(IDbContextFactory<ApplicationDbContext> contextFactory) : IRepository<TModel>
        where TModel: class
    {
        public Task CreateAsync(TModel model, CancellationToken ct)
        {
            return ExecuteInContext(transaction => CreateAsync(transaction, model, ct), ct);
        }

        public async Task CreateAsync(IRepositoryOperation transaction, TModel model, CancellationToken ct)
        {
            try
            {
                await TableSelector(transaction).AddAsync(model, ct);
            }
            catch (Exception ex)
            {
                if (ex.TryGetCancellationException(out var oce))
                {
                    throw oce;
                }
                else throw;
            }
        }

        public Task CreateAsync(IEnumerable<TModel> models, CancellationToken ct)
        {
            return ExecuteInContext(transaction => CreateAsync(transaction, models, ct), ct);
        }

        public async Task CreateAsync(IRepositoryOperation transaction, IEnumerable<TModel> models, CancellationToken ct)
        {
            await TableSelector(transaction).AddRangeAsync(models, ct);
        }

        public Task DeleteAsync(Func<IQueryable<TModel>, IQueryable<TModel>> query, CancellationToken ct)
        {
            return ExecuteInContext(transaction => DeleteAsync(transaction, query, ct), ct);
        }

        public async Task DeleteAsync(IRepositoryOperation transaction, Func<IQueryable<TModel>, IQueryable<TModel>> query, CancellationToken ct)
        {
            var table = TableSelector(transaction);
            table.RemoveRange(query(table));
        }

        public Task<TResult> GetAsync<TResult>(Func<IQueryable<TModel>, Task<TResult>> query, CancellationToken ct)
        {
            return ExecuteInContext(transaction => GetAsync(transaction, query, ct), ct);
        }

        public async Task<TResult> GetAsync<TResult>(IRepositoryOperation transaction, Func<IQueryable<TModel>, Task<TResult>> query, CancellationToken ct)
        {
            return await query(TableSelector(transaction));
        }

        public Task UpdateAsync(Func<IQueryable<TModel>, Task<TModel>> query, CancellationToken ct)
        {
            return ExecuteInContext(transaction => UpdateAsync(transaction, query, ct), ct);
        }

        public async Task UpdateAsync(IRepositoryOperation transaction, Func<IQueryable<TModel>, Task<TModel>> query, CancellationToken ct)
        {
            var table = TableSelector(transaction);
            var toUpdate = await query(table);

        }

        public async Task ExecuteInTransaction(Func<IRepositoryOperation, Task> action, CancellationToken ct)
        {
            await using var transaction = await GetRepositoryTransactionAsync(ct);
            await action(transaction);
            if (!transaction.IsCommitted && !transaction.IsRolledBack) await transaction.CommitAsync(ct);
        }

        public async Task<TReturn> ExecuteInTransaction<TReturn>(Func<IRepositoryOperation, Task<TReturn>> action, CancellationToken ct)
        {
            await using var transaction = await GetRepositoryTransactionAsync(ct);
            var result = await action(transaction);
            if (!transaction.IsCommitted && !transaction.IsRolledBack) await transaction.CommitAsync(ct);

            return result;
        }

        protected virtual async Task ExecuteInContext(Func<IRepositoryOperation, Task> action, CancellationToken ct)
        {
            await using var context = await GetRepositoryContextAsync(ct);
            await action(context);
            if (!context.IsCommitted && !context.IsRolledBack) await context.CommitAsync(ct);
        }

        protected virtual async Task<TReturn> ExecuteInContext<TReturn>(Func<IRepositoryOperation, Task<TReturn>> action, CancellationToken ct)
        {
            await using var context = await GetRepositoryContextAsync(ct);
            var result = await action(context);
            if (!context.IsCommitted && !context.IsRolledBack) await context.CommitAsync(ct);

            return result;
        }

        protected virtual async Task<IRepositoryOperation> GetRepositoryContextAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var ctx = await contextFactory.CreateDbContextAsync(ct);
            return new RepositoryContext<ApplicationDbContext>(ctx);
        }

        protected virtual async Task<IRepositoryOperation> GetRepositoryTransactionAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var ctx = await contextFactory.CreateDbContextAsync(ct);
            var transaction = await ctx.Database.BeginTransactionAsync(ct);
            return new RepositoryTransaction<ApplicationDbContext>(ctx, transaction);
        }

        protected abstract DbSet<TModel> TableSelector(ApplicationDbContext context);

        protected DbSet<TModel> TableSelector(IRepositoryOperation operation)
        {
            return TableSelector((ApplicationDbContext)operation.Context);
        }
    }
}
