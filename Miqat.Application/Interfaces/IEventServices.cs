using Miqat.Application.Common;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Interfaces
{
    public interface IEventService
    {
        /// <summary>The events (each stored once, repeating ones too) of the caller that touch the range.</summary>
        Task<List<EventDto>> GetInRangeAsync(DateRange range, Guid? calendarId);

        Task<EventDto?> GetByIdAsync(Guid id);
        Task<EventDto> CreateAsync(SaveEventDto dto);
        Task<EventDto?> UpdateAsync(Guid id, SaveEventDto dto);
        Task<bool> DeleteAsync(Guid id);

        /// <summary>Changes or skips one date of a repeating event.</summary>
        Task<EventDto?> SaveOccurrenceAsync(Guid id, SaveOccurrenceDto dto);

        /// <summary>Puts one date back to what the series says. False when that date was never changed.</summary>
        Task<bool> RestoreOccurrenceAsync(Guid id, DateTime occurrenceStart);
    }

    public interface IEventAttendeeService
    {
        /// <summary>Events the caller is invited to and has not answered.</summary>
        Task<List<EventDto>> GetInvitationsAsync();

        /// <summary>Owner only. People who are already invited are left as they are.</summary>
        Task<EventDto?> InviteAsync(Guid eventId, IEnumerable<Guid> userIds);

        /// <summary>The owner removes someone, or an invited person removes themselves.</summary>
        Task<bool> RemoveAsync(Guid eventId, Guid userId);

        /// <summary>The answer of the caller to an invitation.</summary>
        Task<EventDto?> RespondAsync(Guid eventId, AttendeeStatus status);

        /// <summary>
        /// Adds invitations to an event that is being created, without saving:
        /// they are stored together with the event. Returns who to tell.
        /// </summary>
        Task<List<Guid>> StageInvitationsAsync(CalendarEvent calendarEvent, IEnumerable<Guid> userIds);

        /// <summary>Tells the invited people, after the event was saved.</summary>
        Task NotifyInvitedAsync(CalendarEvent calendarEvent, IEnumerable<Guid> userIds);
    }
}
