using FluentValidation;
using Miqat.Application.Common;
using Miqat.Application.Modules;
using Miqat.Domain.Enumerations;

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

            RuleFor(x => x.Tags)
                .MaximumLength(500).WithMessage("Tags cannot exceed 500 characters.")
                .When(x => x.Tags != null);

            RuleFor(x => x.Priority)
                .NotEmpty().WithMessage("Priority is required.")
                .Must(p => EnumText.TryParseDefined<Priority>(p, out _))
                .WithMessage("Priority must be Low, Medium, High or Critical.");

            // The enum members are In_progress / On_hold. EnumText also accepts
            // the InProgress / OnHold spellings, and the service parses with the
            // same helper - so a value that passes here can never fail to apply.
            RuleFor(x => x.Status)
                .Must(s => EnumText.TryParseTaskStatus(s, out _))
                .WithMessage("Status must be one of: Pending, In_progress, Completed, Cancelled, On_hold.")
                .When(x => !string.IsNullOrEmpty(x.Status));

            RuleFor(x => x.Recurrence)
                .Must(r => EnumText.TryParseDefined<RecurrencePattern>(r, out _))
                .WithMessage("Recurrence must be None, Daily, Weekly or Monthly.")
                .When(x => !string.IsNullOrEmpty(x.Recurrence));

            RuleFor(x => x.StartDate)
                .LessThanOrEqualTo(x => x.DueDate!.Value)
                .WithMessage("Start date cannot be after the due date.")
                .When(x => x.StartDate.HasValue && x.DueDate.HasValue);

            // "Due date must be in the future" is a create-only rule and lives in
            // TaskService.CreateAsync. Here it would also run on updates - the
            // body of a PUT does not have to carry the id - and make an overdue
            // task impossible to complete or reassign.
        }
    }
}
