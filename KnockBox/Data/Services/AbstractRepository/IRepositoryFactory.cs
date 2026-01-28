namespace KnockBox.Data.Services.AbstractRepository
{
    public interface IRepositoryFactory<TRepository>
        where TRepository : IRepository
    {
        TRepository CreateRepository();
    }
}
