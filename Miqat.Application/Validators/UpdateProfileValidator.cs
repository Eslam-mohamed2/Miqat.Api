using FluentValidation;
using Miqat.Application.Common;
using Miqat.Application.Modules;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Validators
{
    public class UpdateProfileValidator : AbstractValidator<UpdateProfileDto>
    {
        public UpdateProfileValidator()
        {
            RuleFor(x => x.FullName)
                .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Full name is required.")
                .MaximumLength(150).WithMessage("Full name cannot exceed 150 characters.");

            // Column limits: without these an over-long value reaches the
            // database and comes back as a 500.
            RuleFor(x => x.PhoneNumber)
                .MaximumLength(20).WithMessage("Phone number cannot exceed 20 characters.");

            RuleFor(x => x.Country)
                .MaximumLength(100).WithMessage("Country cannot exceed 100 characters.");

            RuleFor(x => x.TimeZone)
                .MaximumLength(100).WithMessage("Time zone cannot exceed 100 characters.");

            RuleFor(x => x.Bio)
                .MaximumLength(500).WithMessage("Bio cannot exceed 500 characters.");

            RuleFor(x => x.Gender)
                .Must(g => EnumText.TryParseDefined<Gender>(g, out _))
                .WithMessage("Gender must be Male, Female or Other.")
                .When(x => !string.IsNullOrWhiteSpace(x.Gender));

            RuleFor(x => x.DateOfBirth)
                .LessThan(_ => DateTime.UtcNow).WithMessage("Date of birth must be in the past.")
                .When(x => x.DateOfBirth.HasValue);
        }
    }
}
