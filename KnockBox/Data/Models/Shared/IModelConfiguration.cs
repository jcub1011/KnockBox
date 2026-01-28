using Microsoft.EntityFrameworkCore;

namespace KnockBox.Data.Models.Shared
{
    public interface IModelConfiguration
    {
        /// <summary>
        /// Adds the configuration for this model to the model builder.
        /// </summary>
        /// <param name="modelBuilder"></param>
        void AddConfiguration(ModelBuilder modelBuilder);
    }
}
