using KnockBox.Data.Models.Shared;
using KnockBox.Data.Models.Testing;
using Microsoft.EntityFrameworkCore;

namespace KnockBox.Data.DbContexts
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
    {
        #region Db Sets

        public DbSet<TestModel> TestModel => Set<TestModel>();

        #endregion

        #region Configurations

        private static IModelConfiguration[] Configurations => [
            new TestModelConfiguration()
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
