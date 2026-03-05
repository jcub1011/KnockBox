using KnockBox.Data.Entities.Shared;
using KnockBox.Data.Entities.Testing;
using Microsoft.EntityFrameworkCore;

namespace KnockBox.Data.DbContexts
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
    {
        #region Db Sets

        public DbSet<TestEntity> TestEntity => Set<TestEntity>();

        #endregion

        #region Configurations

        private static IEntityConfiguration[] Configurations => [
            new TestEntityConfiguration()
        ];

        #endregion

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            foreach (var configuration in Configurations)
            {
                configuration.AddConfiguration(modelBuilder);
            }
        }
    }
}
