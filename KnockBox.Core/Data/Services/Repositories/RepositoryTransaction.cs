using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace KnockBox.Data.Services.Repositories
{
    /// <summary>
    /// A repository operation that writes changes to a single transaction.
    /// </summary>
    /// <typeparam name="TDbContext"></typeparam>
    /// <param name="context"></param>
    /// <param name="transaction"></param>
    public sealed class RepositoryTransaction<TDbContext>(TDbContext context, IDbContextTransaction transaction)
        : IRepositoryOperation
        where TDbContext : DbContext
    {
        private bool _disposed = false;

        public bool IsRolledBack { get; private set; }
        public bool IsCommitted { get; private set; }
        public DbContext Context => context;

        public Guid TransactionId => transaction.TransactionId;

        public void Commit()
        {
            ThrowIfInvalid();

            context.SaveChanges();
            transaction.Commit();
            IsCommitted = true;
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfInvalid();

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            IsCommitted = true;
        }

        public void Dispose()
        {
            context.Dispose();
            transaction.Dispose();

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await context.DisposeAsync();
            await transaction.DisposeAsync();

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public void Rollback()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsRolledBack) throw new InvalidOperationException("Operation could not be performed as this transaction has already been rolled back.");

            transaction.Rollback();
            IsRolledBack = true;
        }

        public async Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsRolledBack) throw new InvalidOperationException("Operation could not be performed as this transaction has already been rolled back.");

            await transaction.RollbackAsync(cancellationToken);
            IsRolledBack = true;
        }

        public void SaveChanges()
        {
            ThrowIfInvalid();

            context.SaveChanges();
        }

        public Task SaveChanges(CancellationToken cancellationToken)
        {
            ThrowIfInvalid();

            return context.SaveChangesAsync(cancellationToken);
        }

        void ThrowIfInvalid()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsCommitted) throw new InvalidOperationException("Operation could not be performed as this transaction has already been committed.");
            if (IsRolledBack) throw new InvalidOperationException("Operation could not be performed as this transaction has already been rolled back.");
        }
    }
}
