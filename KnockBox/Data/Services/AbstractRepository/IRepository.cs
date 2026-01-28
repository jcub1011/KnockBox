namespace KnockBox.Data.Services.AbstractRepository
{
    public interface IRepository<TRepository>
    {
        IRepository<TRepository> AddQuery<T>(Func<IQueryable<T>, IQueryable<T>> query);

        IRepository<TRepository> AddQuery<TInput, TResult>(Func<IQueryable<TInput>, IQueryable<TResult>> query);

        Task<TResult> Execute<TResult>(CancellationToken ct);
    }
}
