using KnockBox.Data.DbContexts;
using KnockBox.Data.Services.KeyProviders;
using KnockBox.Core.Extensions.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace KnockBox.Data.Services.Repositories
{
    public class BaseRepository<TModel>(
        IDbContextFactory<ApplicationDbContext> contextFactory, 
        IEntityKeyProvider<TModel, ApplicationDbContext> keyProvider) 
        : IRepository<TModel>
        where TModel: class
    {
        #region Create

        public Task CreateAsync(TModel model, CancellationToken ct)
        {
            return ExecuteInContext(transaction => CreateAsync(transaction, model, ct), ct);
        }

        public async Task CreateAsync(IRepositoryOperation transaction, TModel model, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await TableSelector(transaction).AddAsync(model, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ex.TryGetCancellationException(out var oce)) throw oce;
                else throw;
            }
        }

        public Task CreateAsync(IEnumerable<TModel> models, CancellationToken ct)
        {
            return ExecuteInContext(transaction => CreateAsync(transaction, models, ct), ct);
        }

        public async Task CreateAsync(IRepositoryOperation transaction, IEnumerable<TModel> models, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                await TableSelector(transaction).AddRangeAsync(models, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ex.TryGetCancellationException(out var oce)) throw oce;
                else throw;
            }
        }

        #endregion

        #region Delete

        public Task DeleteAsync(TModel model, CancellationToken ct)
        {
            return ExecuteInTransaction(transaction => DeleteAsync(transaction, model, ct), ct);
        }

        public async Task DeleteAsync(IRepositoryOperation transaction, TModel model, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var table = TableSelector(transaction);
                table.Remove(model);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ex.TryGetCancellationException(out var oce)) throw oce;
                else throw;
            }
        }

        public Task DeleteAsync(IEnumerable<TModel> models, CancellationToken ct)
        {
            return ExecuteInTransaction(transaction => DeleteAsync(transaction, models, ct), ct);
        }

        public async Task DeleteAsync(IRepositoryOperation transaction, IEnumerable<TModel> models, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var table = TableSelector(transaction);
                table.RemoveRange(models);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ex.TryGetCancellationException(out var oce)) throw oce;
                else throw;
            }
        }

        public Task DeleteAsync(Func<IQueryable<TModel>, IQueryable<TModel>> query, CancellationToken ct)
        {
            return ExecuteInContext(transaction => DeleteAsync(transaction, query, ct), ct);
        }

        public async Task DeleteAsync(IRepositoryOperation transaction, Func<IQueryable<TModel>, IQueryable<TModel>> query, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var table = TableSelector(transaction);
                var models = await query(table).ToArrayAsync(ct);
                table.RemoveRange(models);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ex.TryGetCancellationException(out var oce)) throw oce;
                else throw;
            }
        }

        #endregion

        #region Update

        public Task UpdateAsync(TModel model, CancellationToken ct)
        {
            return ExecuteInContext(transaction => UpdateAsync(transaction, model, ct), ct);
        }

        public async Task UpdateAsync(IRepositoryOperation transaction, TModel model, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var table = TableSelector(transaction);
                table.Update(model);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ex.TryGetCancellationException(out var oce)) throw oce;
                else throw;
            }
        }

        public Task UpdateAsync(IEnumerable<TModel> models, CancellationToken ct)
        {
            return ExecuteInContext(transaction => UpdateAsync(transaction, models, ct), ct);
        }

        public async Task UpdateAsync(IRepositoryOperation transaction, IEnumerable<TModel> models, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var table = TableSelector(transaction);
                table.UpdateRange(models);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ex.TryGetCancellationException(out var oce)) throw oce;
                else throw;
            }
        }

        public Task UpdateAsync(Func<IQueryable<TModel>, Task<TModel>> query, Action<TModel> updateAction, CancellationToken ct)
        {
            return ExecuteInContext(transaction => UpdateAsync(transaction, query, updateAction, ct), ct);
        }

        public async Task UpdateAsync(IRepositoryOperation transaction, Func<IQueryable<TModel>, Task<TModel>> query, Action<TModel> updateAction, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var table = TableSelector(transaction);
                var toUpdate = await query(table);
                updateAction(toUpdate);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ex.TryGetCancellationException(out var oce)) throw oce;
                else throw;
            }
        }

        public Task UpdateAsync(Func<IQueryable<TModel>, IQueryable<TModel>> query, Action<TModel> updateAction, CancellationToken ct)
        {
            return ExecuteInContext(transaction => UpdateAsync(transaction, query, updateAction, ct), ct);
        }

        public async Task UpdateAsync(IRepositoryOperation transaction, Func<IQueryable<TModel>, IQueryable<TModel>> query, Action<TModel> updateAction, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var table = TableSelector(transaction);
                var toUpdate = await query(table).ToArrayAsync(ct);

                foreach (var model in toUpdate) updateAction(model);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ex.TryGetCancellationException(out var oce)) throw oce;
                else throw;
            }
        }

        #endregion

        #region Get Methods

        public Task<TResult> GetAsync<TResult>(Func<IQueryable<TModel>, Task<TResult>> query, CancellationToken ct)
        {
            return ExecuteInContext(transaction => GetAsync(transaction, query, ct), ct);
        }

        public async Task<TResult> GetAsync<TResult>(IRepositoryOperation transaction, Func<IQueryable<TModel>, Task<TResult>> query, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                return await query(TableSelector(transaction));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ex.TryGetCancellationException(out var oce)) throw oce;
                else throw;
            }
        }

        #endregion

        #region Transaction Methods

        public async Task ExecuteInTransaction(Func<IRepositoryOperation, Task> action, CancellationToken ct)
        {
            try
            {
                await using var transaction = await GetRepositoryTransactionAsync(ct);
                await action(transaction);
                if (!transaction.IsCommitted && !transaction.IsRolledBack) await transaction.CommitAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ex.TryGetCancellationException(out var oce)) throw oce;
                else throw;
            }
        }

        public async Task<TReturn> ExecuteInTransaction<TReturn>(Func<IRepositoryOperation, Task<TReturn>> action, CancellationToken ct)
        {
            try
            {
                await using var transaction = await GetRepositoryTransactionAsync(ct);
                var result = await action(transaction);
                if (!transaction.IsCommitted && !transaction.IsRolledBack) await transaction.CommitAsync(ct);

                return result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ex.TryGetCancellationException(out var oce)) throw oce;
                else throw;
            }
        }

        public async Task ExecuteInContext(Func<IRepositoryOperation, Task> action, CancellationToken ct)
        {
            try
            {
                await using var context = await GetRepositoryContextAsync(ct);
                await action(context);
                if (!context.IsCommitted && !context.IsRolledBack) await context.CommitAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ex.TryGetCancellationException(out var oce)) throw oce;
                else throw;
            }
        }

        public async Task<TReturn> ExecuteInContext<TReturn>(Func<IRepositoryOperation, Task<TReturn>> action, CancellationToken ct)
        {
            try
            {
                await using var context = await GetRepositoryContextAsync(ct);
                var result = await action(context);
                if (!context.IsCommitted && !context.IsRolledBack) await context.CommitAsync(ct);

                return result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (ex.TryGetCancellationException(out var oce)) throw oce;
                else throw;
            }
        }

        #endregion

        #region Helper Methods

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

        protected DbSet<TModel> TableSelector(IRepositoryOperation operation)
        {
            return keyProvider.GetTable((ApplicationDbContext)operation.Context);
        }

        #endregion
    }
}
