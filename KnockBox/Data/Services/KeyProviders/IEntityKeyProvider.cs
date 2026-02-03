using Microsoft.EntityFrameworkCore;

namespace KnockBox.Data.Services.KeyProviders
{
    public interface IEntityKeyProvider<TModel, TDatabaseContext>
        where TDatabaseContext : DbContext
        where TModel : class
    {
        /// <summary>
        /// Gets the table associated with this model from the database.
        /// </summary>
        /// <param name="context"></param>
        /// <returns></returns>
        DbSet<TModel> GetTable(TDatabaseContext context);
    }
}
