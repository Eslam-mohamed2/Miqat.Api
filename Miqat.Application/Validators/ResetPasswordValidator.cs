using FluentValidation;
using Miqat.Application.Modules;

namespace Miqat.Application.Validators
{
    public class ResetPasswordValidator : AbstractValidator<ResetPasswordDto>
    {
        public ResetPasswordValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required.")
                .EmailAddress().WithMessage("Invalid email format.");

            RuleFor(x => x.Token)
                .NotEmpty().WithMessage("Token is required.");

            RuleFor(x => x.NewPassword)
                .NotEmpty().WithMessage("Password is required.")
                .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
                .MaximumLength(128).WithMessage("Password cannot exceed 128 characters.")
                .Matches("[A-Z]").WithMessage("Must contain uppercase letter.")
                .Matches("[0-9]").WithMessage("Must contain a number.")
                .Matches("[^a-zA-Z0-9]").WithMessage("Must contain a special character.");

            RuleFor(x => x.ConfirmPassword)
                .Equal(x => x.NewPassword).WithMessage("Passwords do not match.");
        }
    }
}
