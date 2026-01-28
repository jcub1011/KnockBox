namespace KnockBox.Data.Services.Repositories
{
    public interface IRepositoryFactory<TRepository>
        where TRepository : IRepository
    {
        TRepository CreateRepository();
    }
}
