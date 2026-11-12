using Miqat.Application.Modules;
using Miqat.Domain.Entities;

namespace Miqat.Application.Common;

/// <summary>
/// CalendarEvent -> EventDto, as one person sees it. Related values are read
/// through null-safe navigations, like the other hand-written mappers.
/// </summary>
public static class EventMapper
{
    public static EventDto ToDto(CalendarEvent calendarEvent, Guid viewerId)
    {
        var isOwner = calendarEvent.OwnerId == viewerId;

        return new EventDto
        {
            Id = calendarEvent.Id,
            Title = calendarEvent.Title,
            Description = calendarEvent.Description,
            Location = calendarEvent.Location,
            StartsAt = calendarEvent.StartsAt,
            EndsAt = calendarEvent.EndsAt,
            IsAllDay = calendarEvent.IsAllDay,
            Recurrence = calendarEvent.Recurrence,
            RecurrenceInterval = calendarEvent.RecurrenceInterval,
            RecurrenceEndDate = calendarEvent.RecurrenceEndDate,

            // Which of their calendars the owner filed it under is their own
            // business; other people only get the colour to draw it with.
            CalendarId = calendarEvent.CalendarId,
            CalendarName = isOwner ? calendarEvent.Calendar?.Name : null,
            Color = calendarEvent.Calendar?.Color,

            GroupId = calendarEvent.GroupId,
            GroupName = calendarEvent.Group?.Name,

            OwnerId = calendarEvent.OwnerId,
            OwnerName = calendarEvent.Owner?.FullName,
            OwnerProfilePictureUrl = calendarEvent.Owner?.ProfilePictureUrl,
            IsOwner = isOwner,

            MyResponse = calendarEvent.Attendees.FirstOrDefault(a => a.UserId == viewerId)?.Status,
            Attendees = calendarEvent.Attendees
                .OrderBy(a => a.User?.FullName)
                .Select(ToDto)
                .ToList(),

            CreatedAt = calendarEvent.CreatedAt,
            UpdatedAt = calendarEvent.UpdatedAt
        };
    }

    public static AttendeeDto ToDto(EventAttendee attendee) => new()
    {
        UserId = attendee.UserId,
        FullName = attendee.User?.FullName,
        ProfilePictureUrl = attendee.User?.ProfilePictureUrl,
        Status = attendee.Status,
        RespondedAt = attendee.RespondedAt
    };
}
