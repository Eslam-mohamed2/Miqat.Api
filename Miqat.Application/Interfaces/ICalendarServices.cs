using Miqat.Application.Common;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;

namespace Miqat.Application.Interfaces
{
    /// <summary>The calendars of the caller ("Personal", "Work", ...) and their calendar preferences.</summary>
    public interface ICalendarService
    {
        Task<List<CalendarDto>> GetMineAsync();
        Task<CalendarDto> CreateAsync(SaveCalendarDto dto);
        Task<CalendarDto?> UpdateAsync(Guid id, SaveCalendarDto dto);

        /// <summary>Removes a calendar; its events move to the default calendar. The default one cannot be removed.</summary>
        Task<bool> DeleteAsync(Guid id);

        Task<CalendarSettingsDto> GetSettingsAsync();
        Task<CalendarSettingsDto> SaveSettingsAsync(CalendarSettingsDto dto);

        /// <summary>
        /// The calendar an event should go into: the one asked for (it must
        /// belong to the user), or their default calendar - created the first
        /// time it is needed.
        /// </summary>
        Task<UserCalendar> ResolveAsync(Guid userId, Guid? calendarId);

        /// <summary>The stored preferences of a user, or the defaults when they never saved any.</summary>
        Task<CalendarSettings> GetSettingsOfAsync(Guid userId);
    }

    /// <summary>Everything on the calendar of the caller between two moments, as one list.</summary>
    public interface ICalendarFeedService
    {
        Task<List<CalendarItemDto>> GetFeedAsync(DateRange range, IReadOnlyCollection<Guid>? calendarIds, bool includeTasks);
    }

    /// <summary>Moving a calendar in and out of Miqat as an .ics file (the format Google, Outlook and Apple all read).</summary>
    public interface ICalendarFileService
    {
        /// <summary>The events of the caller as .ics text; one calendar, or all of them.</summary>
        Task<string> ExportAsync(Guid? calendarId);

        /// <summary>Adds the events of an .ics file to a calendar of the caller. Importing the same file again updates them.</summary>
        Task<ImportResultDto> ImportAsync(Stream file, Guid? calendarId);
    }
}
