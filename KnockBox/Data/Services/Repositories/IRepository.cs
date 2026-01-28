using System.Linq.Expressions;

namespace KnockBox.Data.Services.Repositories
{
    public interface IRepository<TModel>
    {
        /// <summary>
        /// Gets the result of the query.
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="query"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<TResult> GetAsync<TResult>(Expression<Func<IQueryable<TModel>, IQueryable<TResult>>> query, CancellationToken ct);

        /// <summary>
        /// Creates the models in the database.
        /// </summary>
        /// <param name="models"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task CreateAsync(IEnumerable<TModel> models, CancellationToken ct);

        /// <summary>
        /// Updates the models in the database.
        /// </summary>
        /// <param name="models"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task UpdateAsync(IEnumerable<TModel> models, CancellationToken ct);

        /// <summary>
        /// Deletes the models in the database.
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<int> DeleteAsync(IEnumerable<TModel> models, CancellationToken ct);
    }
}
