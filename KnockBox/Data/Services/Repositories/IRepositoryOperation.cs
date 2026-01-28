using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace KnockBox.Data.Services.Repositories
{
    /// <summary>
    /// An repository operation context for repositories.
    /// </summary>
    public interface IRepositoryOperation : IDbContextTransaction
    {
        /// <summary>
        /// If the transaction has been rolled back.
        /// </summary>
        bool IsRolledBack { get; }

        /// <summary>
        /// If the transaction has been commmitted.
        /// </summary>
        bool IsCommitted { get; }

        /// <summary>
        /// The context used by this operation.
        /// </summary>
        internal DbContext Context { get; }

        /// <summary>
        /// Saves the changes to the database without committing the transaction.
        /// </summary>
        void SaveChanges();

        /// <summary>
        /// Saves the changes to the database without committing the transaction.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        Task SaveChanges(CancellationToken cancellationToken = default);
    }
}
