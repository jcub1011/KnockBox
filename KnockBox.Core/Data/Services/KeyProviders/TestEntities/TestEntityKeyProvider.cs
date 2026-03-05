using KnockBox.Data.DbContexts;
using KnockBox.Data.Entities.Testing;
using Microsoft.EntityFrameworkCore;

namespace KnockBox.Data.Services.KeyProviders.TestEntities
{
    public class TestEntityKeyProvider : IEntityKeyProvider<TestEntity, ApplicationDbContext>
    {
        public DbSet<TestEntity> GetTable(ApplicationDbContext context) => context.TestEntity;
    }
}
