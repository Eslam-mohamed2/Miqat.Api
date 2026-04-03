using FluentValidation;
using Miqat.Application.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Validators
{
    public class TaskValidator : AbstractValidator<TaskDto>
    {
        public TaskValidator()
        {
            RuleFor(x => x.Title)
                .NotEmpty().WithMessage("Title is required.")
                .MaximumLength(200).WithMessage("Title cannot exceed 200 characters.");

            RuleFor(x => x.Description)
                .MaximumLength(1000).WithMessage("Description cannot exceed 1000 characters.")
                .When(x => x.Description != null);

            RuleFor(x => x.Priority)
                .NotEmpty().WithMessage("Priority is required.")
                .Must(p => new[] { "Low", "Medium", "High", "Critical" }.Contains(p))
                .WithMessage("Priority must be Low, Medium, High or Critical.");

            RuleFor(x => x.Status)
                .Must(s => new[] { "Pending", "InProgress", "Completed", "Cancelled" }.Contains(s))
                .WithMessage("Invalid status value.")
                .When(x => !string.IsNullOrEmpty(x.Status));

            RuleFor(x => x.DueDate)
                .GreaterThan(DateTime.UtcNow).WithMessage("Due date must be in the future.")
                .When(x => x.DueDate.HasValue);
        }
    }
}
