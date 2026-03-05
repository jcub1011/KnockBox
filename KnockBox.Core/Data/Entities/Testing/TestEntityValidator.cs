using FluentValidation;

namespace KnockBox.Data.Entities.Testing
{
    public class TestEntityValidator : AbstractValidator<TestEntity>
    {
        public TestEntityValidator()
        {
            RuleFor(x => x.TestDate).GreaterThan(DateTime.MinValue);
            RuleFor(x => x.TestData).MinimumLength(1).MaximumLength(16);
        }
    }
}
