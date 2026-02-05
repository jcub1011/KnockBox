using KnockBox.Data.Entities.Shared;
using Microsoft.EntityFrameworkCore;

namespace KnockBox.Data.Entities.Testing
{
    public class TestEntityConfiguration : IEntityConfiguration
    {
        public void AddConfiguration(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestEntity>();
        }
    }
}
