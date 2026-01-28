
namespace KnockBox.Data.Services.AbstractRepository
{
    public class AbstractRepository<TModel> : IRepository<TModel>
    {
        private readonly Stack<Func<IQueryable, IQueryable>> _queryStack = [];

        public IRepository<TModel> AddQuery<T>(Func<IQueryable<T>, IQueryable<T>> query)
        {
            _queryStack.Push(query);
        }

        public IRepository<TModel> AddQuery<TInput, TResult>(Func<IQueryable<TInput>, IQueryable<TResult>> query)
        {
            throw new NotImplementedException();
        }

        public Task<TResult> Execute<TResult>(CancellationToken ct)
        {

        }
    }
}
