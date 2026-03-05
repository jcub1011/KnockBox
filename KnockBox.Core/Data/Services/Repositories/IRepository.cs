namespace KnockBox.Data.Services.Repositories
{
    public interface IRepository<TModel>
    {
        #region Create

        /// <summary>
        /// Creates the model in the database.
        /// </summary>
        /// <param name="model"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task CreateAsync(TModel model, CancellationToken ct);

        /// <summary>
        /// Creates the model in the database.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="model"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task CreateAsync(IRepositoryOperation transaction, TModel model, CancellationToken ct);

        /// <summary>
        /// Creates the models in the database.
        /// </summary>
        /// <param name="models"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task CreateAsync(IEnumerable<TModel> models, CancellationToken ct);

        /// <summary>
        /// Creates the models in the database.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="models"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task CreateAsync(IRepositoryOperation transaction, IEnumerable<TModel> models, CancellationToken ct);

        #endregion

        #region Delete

        /// <summary>
        /// Deletes the model in the database.
        /// </summary>
        /// <param name="model"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task DeleteAsync(TModel model, CancellationToken ct);

        /// <summary>
        /// Deletes the model in the database.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="model"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task DeleteAsync(IRepositoryOperation transaction, TModel model, CancellationToken ct);

        /// <summary>
        /// Deletes the models in the database.
        /// </summary>
        /// <param name="models"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task DeleteAsync(IEnumerable<TModel> models, CancellationToken ct);

        /// <summary>
        /// Deletes the models in the database.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="models"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task DeleteAsync(IRepositoryOperation transaction, IEnumerable<TModel> models, CancellationToken ct);

        /// <summary>
        /// Deletes the models matching the query in the database.
        /// </summary>
        /// <param name="query"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task DeleteAsync(Func<IQueryable<TModel>, IQueryable<TModel>> query, CancellationToken ct);

        /// <summary>
        /// Deletes the models matching the query in the database.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="query"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task DeleteAsync(IRepositoryOperation transaction, Func<IQueryable<TModel>, IQueryable<TModel>> query, CancellationToken ct);

        #endregion

        #region Update 

        /// <summary>
        /// Updates the model in the database.
        /// </summary>
        /// <param name="model"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task UpdateAsync(TModel model, CancellationToken ct);

        /// <summary>
        /// Updates the model in the database.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="model"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task UpdateAsync(IRepositoryOperation transaction, TModel model, CancellationToken ct);

        /// <summary>
        /// Updates the models in the database.
        /// </summary>
        /// <param name="models"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task UpdateAsync(IEnumerable<TModel> models, CancellationToken ct);

        /// <summary>
        /// Updates the models in the database.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="models"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task UpdateAsync(IRepositoryOperation transaction, IEnumerable<TModel> models, CancellationToken ct);

        /// <summary>
        /// Updates the model matching the query.
        /// </summary>
        /// <param name="query"></param>
        /// <param name="updateAction"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task UpdateAsync(Func<IQueryable<TModel>, Task<TModel>> query, Action<TModel> updateAction, CancellationToken ct);

        /// <summary>
        /// Updates the model matching the query.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="query"></param>
        /// <param name="updateAction"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task UpdateAsync(IRepositoryOperation transaction, Func<IQueryable<TModel>, Task<TModel>> query, Action<TModel> updateAction, CancellationToken ct);

        /// <summary>
        /// Updates the models matching the query.
        /// </summary>
        /// <param name="query"></param>
        /// <param name="updateAction"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task UpdateAsync(Func<IQueryable<TModel>, IQueryable<TModel>> query, Action<TModel> updateAction, CancellationToken ct);

        /// <summary>
        /// Updates the models matching the query.
        /// </summary>
        /// <param name="transaction"></param>
        /// <param name="query"></param>
        /// <param name="updateAction"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task UpdateAsync(IRepositoryOperation transaction, Func<IQueryable<TModel>, IQueryable<TModel>> query, Action<TModel> updateAction, CancellationToken ct);

        #endregion

        #region Get

        /// <summary>
        /// Gets the models matching the query.
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="query"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<TResult> GetAsync<TResult>(Func<IQueryable<TModel>, Task<TResult>> query, CancellationToken ct);

        /// <summary>
        /// Gets the models matching the query.
        /// </summary>
        /// <typeparam name="TResult"></typeparam>
        /// <param name="transaction"></param>
        /// <param name="query"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<TResult> GetAsync<TResult>(IRepositoryOperation transaction, Func<IQueryable<TModel>, Task<TResult>> query, CancellationToken ct);

        #endregion

        #region Transaction

        /// <summary>
        /// Executes the action within a single transaction.
        /// </summary>
        /// <param name="action"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task ExecuteInTransaction(Func<IRepositoryOperation, Task> action, CancellationToken ct);

        /// <summary>
        /// Executes the transaction within a single transaction.
        /// </summary>
        /// <typeparam name="TReturn"></typeparam>
        /// <param name="action"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<TReturn> ExecuteInTransaction<TReturn>(Func<IRepositoryOperation, Task<TReturn>> action, CancellationToken ct);

        /// <summary>
        /// Executes the action within a single operation.
        /// </summary>
        /// <param name="action"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task ExecuteInContext(Func<IRepositoryOperation, Task> action, CancellationToken ct);

        /// <summary>
        /// Executes the transaction within a single operation.
        /// </summary>
        /// <typeparam name="TReturn"></typeparam>
        /// <param name="action"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<TReturn> ExecuteInContext<TReturn>(Func<IRepositoryOperation, Task<TReturn>> action, CancellationToken ct);

        #endregion
    }
}
