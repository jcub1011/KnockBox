using KnockBox.Data.Models.Shared;
using Microsoft.EntityFrameworkCore;

namespace KnockBox.Data.Models.Testing
{
    public class TestModelConfiguration : IModelConfiguration
    {
        public void AddConfiguration(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestModel>();
        }
    }
}
