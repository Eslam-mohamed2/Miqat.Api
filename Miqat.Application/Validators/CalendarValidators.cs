using FluentValidation;
using Miqat.Application.Common;
using Miqat.Application.Modules;
using Miqat.Application.Services;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Validators
{
    // Column limits are checked here so an over-long value answers 400 with a
    // sentence, instead of reaching the database and coming back as a 500.

    public class SaveCalendarValidator : AbstractValidator<SaveCalendarDto>
    {
        public SaveCalendarValidator()
        {
            RuleFor(x => x.Name)
                .MaximumLength(80).WithMessage("Name cannot exceed 80 characters.");

            RuleFor(x => x.Color)
                .Matches("^#[0-9A-Fa-f]{6}$").WithMessage("Color must be a valid hex color e.g. #FF5733.")
                .When(x => !string.IsNullOrWhiteSpace(x.Color));
        }
    }

    public class CalendarSettingsValidator : AbstractValidator<CalendarSettingsDto>
    {
        public CalendarSettingsValidator()
        {
            RuleFor(x => x.WeekStartsOn).IsInEnum().WithMessage("WeekStartsOn must be a day of the week.");
            RuleFor(x => x.DefaultView).IsInEnum().WithMessage("DefaultView must be Month, Week, Day or Agenda.");

            RuleFor(x => x.WorkDays)
                .NotNull().WithMessage("WorkDays is required (it may be empty).");
            RuleForEach(x => x.WorkDays).IsInEnum().WithMessage("WorkDays must be days of the week.");

            RuleFor(x => x.WorkDayStart)
                .Must(t => ClockTime.ToMinutes(t).HasValue).WithMessage("WorkDayStart must be a time like 09:00.");
            RuleFor(x => x.WorkDayEnd)
                .Must(t => ClockTime.ToMinutes(t).HasValue).WithMessage("WorkDayEnd must be a time like 17:00.");
            RuleFor(x => x)
                .Must(x => ClockTime.ToMinutes(x.WorkDayStart) < ClockTime.ToMinutes(x.WorkDayEnd))
                .WithMessage("The working day must end after it starts.")
                .When(x => ClockTime.ToMinutes(x.WorkDayStart).HasValue && ClockTime.ToMinutes(x.WorkDayEnd).HasValue);

            RuleFor(x => x.DefaultEventMinutes)
                .InclusiveBetween(5, 24 * 60).WithMessage("DefaultEventMinutes must be between 5 minutes and 24 hours.");

            RuleFor(x => x.DefaultReminderMinutes!.Value)
                .InclusiveBetween(0, ReminderService.MaxMinutesBefore)
                .WithMessage("DefaultReminderMinutes must be between 0 minutes and 4 weeks.")
                .When(x => x.DefaultReminderMinutes.HasValue);
        }
    }

    public class SaveEventValidator : AbstractValidator<SaveEventDto>
    {
        private const int MaxDays = 31;
        private const int MaxInterval = 99;

        public SaveEventValidator()
        {
            RuleFor(x => x.Title)
                .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Title is required.")
                .MaximumLength(200).WithMessage("Title cannot exceed 200 characters.");

            RuleFor(x => x.Description)
                .MaximumLength(2000).WithMessage("Description cannot exceed 2000 characters.");

            RuleFor(x => x.Location)
                .MaximumLength(300).WithMessage("Location cannot exceed 300 characters.");

            RuleFor(x => x.EndsAt)
                .GreaterThan(x => x.StartsAt).WithMessage("An event must end after it starts.")
                .LessThanOrEqualTo(x => x.StartsAt.AddDays(MaxDays))
                .WithMessage($"An event cannot be longer than {MaxDays} days.");

            RuleFor(x => x.Recurrence).IsInEnum()
                .WithMessage("Recurrence must be None, Daily, Weekly, Monthly or Yearly.");

            RuleFor(x => x.RecurrenceInterval)
                .InclusiveBetween(1, MaxInterval).WithMessage($"RecurrenceInterval must be between 1 and {MaxInterval}.");

            RuleFor(x => x.RecurrenceEndDate!.Value)
                .GreaterThanOrEqualTo(x => x.StartsAt.Date)
                .WithMessage("The repeat cannot end before the event starts.")
                .When(x => x.RecurrenceEndDate.HasValue && x.Recurrence != RecurrencePattern.None);

            RuleForEach(x => x.ReminderMinutes)
                .InclusiveBetween(0, ReminderService.MaxMinutesBefore)
                .WithMessage("A reminder can be set from 0 minutes up to 4 weeks before.");
        }
    }

    public class SaveOccurrenceValidator : AbstractValidator<SaveOccurrenceDto>
    {
        public SaveOccurrenceValidator()
        {
            RuleFor(x => x.OccurrenceStart)
                .NotEmpty().WithMessage("OccurrenceStart is required: it says which date of the event is meant.");

            RuleFor(x => x.EndsAt!.Value)
                .GreaterThan(x => x.StartsAt!.Value).WithMessage("An event must end after it starts.")
                .When(x => x.StartsAt.HasValue && x.EndsAt.HasValue);

            RuleFor(x => x.Title).MaximumLength(200).WithMessage("Title cannot exceed 200 characters.");
            RuleFor(x => x.Location).MaximumLength(300).WithMessage("Location cannot exceed 300 characters.");
        }
    }

    public class RespondToEventValidator : AbstractValidator<RespondToEventDto>
    {
        public RespondToEventValidator()
        {
            RuleFor(x => x.Status).IsInEnum().WithMessage("Status must be Accepted, Tentative or Declined.");
        }
    }

    public class SuggestTimesValidator : AbstractValidator<SuggestTimesDto>
    {
        public SuggestTimesValidator()
        {
            RuleFor(x => x.AttendeeIds).NotNull().WithMessage("AttendeeIds is required (it may be empty).");

            RuleFor(x => x.DurationMinutes)
                .InclusiveBetween(5, 24 * 60).WithMessage("DurationMinutes must be between 5 minutes and 24 hours.");
        }
    }

    public class RegisterDeviceValidator : AbstractValidator<RegisterDeviceDto>
    {
        public RegisterDeviceValidator()
        {
            RuleFor(x => x.Endpoint)
                .NotEmpty().WithMessage("Endpoint is required.")
                .MaximumLength(1000).WithMessage("Endpoint cannot exceed 1000 characters.");

            RuleFor(x => x.P256dh)
                .NotEmpty().WithMessage("P256dh is required.")
                .MaximumLength(200).WithMessage("P256dh cannot exceed 200 characters.");

            RuleFor(x => x.Auth)
                .NotEmpty().WithMessage("Auth is required.")
                .MaximumLength(100).WithMessage("Auth cannot exceed 100 characters.");

            RuleFor(x => x.Name).MaximumLength(100).WithMessage("Name cannot exceed 100 characters.");
        }
    }

    public class ChangeEmailValidator : AbstractValidator<ChangeEmailDto>
    {
        public ChangeEmailValidator()
        {
            RuleFor(x => x.NewEmail)
                .NotEmpty().WithMessage("New email is required.")
                .EmailAddress().WithMessage("New email is not a valid email address.")
                .MaximumLength(255).WithMessage("Email cannot exceed 255 characters.");

            RuleFor(x => x.CurrentPassword)
                .NotEmpty().WithMessage("Current password is required.");
        }
    }

    public class ConfirmEmailChangeValidator : AbstractValidator<ConfirmEmailChangeDto>
    {
        public ConfirmEmailChangeValidator()
        {
            RuleFor(x => x.Code).NotEmpty().WithMessage("Code is required.");
        }
    }
}
