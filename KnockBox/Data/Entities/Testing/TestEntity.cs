using KnockBox.Data.Schemas;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KnockBox.Data.Entities.Testing
{
    [Table(nameof(TestEntity), Schema = DatabaseSchemas.Public)]
    public record class TestEntity
    {
        [Key]
        public int TestModalId { get; set; }

        [MaxLength(16)]
        public string TestData { get; set; } = string.Empty;

        [Precision(0)]
        public DateTime TestDate { get; set; }
    }
}
