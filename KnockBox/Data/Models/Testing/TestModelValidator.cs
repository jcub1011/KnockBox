using FluentValidation;

namespace KnockBox.Data.Models.Testing
{
    public class TestModelValidator : AbstractValidator<TestModel>
    {
        public TestModelValidator()
        {
            RuleFor(x => x.TestDate).GreaterThan(DateTime.MinValue);
            RuleFor(x => x.TestData).MinimumLength(1).MaximumLength(16);
        }
    }
}
