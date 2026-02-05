using Microsoft.EntityFrameworkCore;

namespace KnockBox.Data.Entities.Shared
{
    public interface IEntityConfiguration
    {
        /// <summary>
        /// Adds the configuration for this model to the model builder.
        /// </summary>
        /// <param name="modelBuilder"></param>
        void AddConfiguration(ModelBuilder modelBuilder);
    }
}
