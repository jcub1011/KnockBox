using Microsoft.EntityFrameworkCore;

namespace KnockBox.Data.Services.Repositories
{
    /// <summary>
    /// A repository operation that writes directly to the database.
    /// </summary>
    /// <typeparam name="TDbContext"></typeparam>
    /// <param name="context"></param>
    public sealed class RepositoryContext<TDbContext>(TDbContext context)
        : IRepositoryOperation
        where TDbContext : DbContext
    {
        private bool _disposed = false;

        public bool IsRolledBack => false;
        public bool IsCommitted { get; private set; }
        public DbContext Context => context;

        public Guid TransactionId => Guid.Empty;

        public void Commit()
        {
            ThrowIfInvalid();

            context.SaveChanges();
            IsCommitted = true;
        }

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfInvalid();

            await context.SaveChangesAsync(cancellationToken);
            IsCommitted = true;
        }

        public void Dispose()
        {
            context.Dispose();

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public async ValueTask DisposeAsync()
        {
            await context.DisposeAsync();

            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public void Rollback()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsRolledBack) throw new InvalidOperationException("Operation could not be performed as this transaction has already been rolled back.");
            throw new InvalidOperationException($"{nameof(RepositoryContext<>)} does not support rollback.");
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsRolledBack) throw new InvalidOperationException("Operation could not be performed as this transaction has already been rolled back.");
            throw new InvalidOperationException($"{nameof(RepositoryContext<>)} does not support rollback.");
        }

        public void SaveChanges()
        {
            ThrowIfInvalid();

            context.SaveChanges();
        }

        public Task SaveChanges(CancellationToken cancellationToken = default)
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
