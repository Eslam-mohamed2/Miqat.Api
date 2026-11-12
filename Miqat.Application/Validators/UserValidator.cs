using FluentValidation;
using Miqat.Application.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Validators
{
    public class UserValidator : AbstractValidator<UserDto>
    {
        public UserValidator()
        {
            RuleFor(x => x.FullName)
                .NotEmpty().WithMessage("Full name is required.")
                .MaximumLength(150).WithMessage("Full name cannot exceed 150 characters.");

            // Optional: this DTO is the body of the admin edit, and an admin form
            // that does not carry the email must not be rejected. A blank value
            // keeps the current address (see UserService.UpdateAsync).
            RuleFor(x => x.Email)
                .EmailAddress().WithMessage("Invalid email format.")
                .MaximumLength(255).WithMessage("Email cannot exceed 255 characters.")
                .When(x => !string.IsNullOrWhiteSpace(x.Email));

            RuleFor(x => x.PhoneNumber)
                .MaximumLength(20).WithMessage("Phone number cannot exceed 20 characters.")
                .When(x => x.PhoneNumber != null);

            RuleFor(x => x.DateOfBirth)
                .LessThan(DateTime.UtcNow).WithMessage("Date of birth must be in the past.")
                .When(x => x.DateOfBirth.HasValue);
        }
    }
}
