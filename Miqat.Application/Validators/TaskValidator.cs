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

            // Derived from the enum rather than hardcoded. The old literal list said
            // "InProgress" while the enum member is "In_progress", and omitted
            // "On_hold" entirely — so the API emitted names via ToString() that it
            // then refused to accept back, and "InProgress" passed validation only to
            // fail Enum.TryParse downstream and silently leave the status unchanged.
            RuleFor(x => x.Status)
                .Must(s => Enum.TryParse<Domain.Enumerations.TaskStatus>(s, ignoreCase: true, out _))
                .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames<Domain.Enumerations.TaskStatus>())}.")
                .When(x => !string.IsNullOrEmpty(x.Status));

            // Only enforced when creating (no Id yet). On update this rule made every
            // task unmodifiable the moment its due date passed — an overdue task could
            // not be completed, reassigned, or moved, which is exactly when you most
            // need to touch it.
            RuleFor(x => x.DueDate)
                .GreaterThan(DateTime.UtcNow).WithMessage("Due date must be in the future.")
                .When(x => x.DueDate.HasValue && x.Id == Guid.Empty);
        }
    }
}
