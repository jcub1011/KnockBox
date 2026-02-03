using KnockBox.Data.DbContexts;
using KnockBox.Data.Models.Testing;
using Microsoft.EntityFrameworkCore;

namespace KnockBox.Data.Services.KeyProviders.TestModels
{
    public class TestModelKeyProvider : IEntityKeyProvider<TestModel, ApplicationDbContext>
    {
        public DbSet<TestModel> GetTable(ApplicationDbContext context) => context.TestModel;
    }
}
