using FluentValidation;
using Miqat.Application.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Validators
{
    public class GroupValidator : AbstractValidator<GroupDto>
    {
        public GroupValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Group name is required.")
                .MaximumLength(150).WithMessage("Name cannot exceed 150 characters.");

            RuleFor(x => x.Description)
                .MaximumLength(500).WithMessage("Description cannot exceed 500 characters.")
                .When(x => x.Description != null);

            RuleFor(x => x.Color)
                .Matches("^#[0-9A-Fa-f]{6}$").WithMessage("Color must be a valid hex color e.g. #FF5733.")
                .When(x => x.Color != null);
        }
    }
}
