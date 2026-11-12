using System.Text;
using Microsoft.AspNetCore.Mvc;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Services;

namespace Miqat.API.Controllers
{
    /// <summary>The calendars of the caller, their calendar preferences, the calendar feed and .ics import / export.</summary>
    public class CalendarController : BaseApiController
    {
        private readonly ICalendarService _calendars;
        private readonly ICalendarFeedService _feed;
        private readonly ICalendarFileService _files;

        public CalendarController(ICalendarService calendars, ICalendarFeedService feed, ICalendarFileService files)
        {
            _calendars = calendars;
            _feed = feed;
            _files = files;
        }

        /// <summary>
        /// Everything on the calendar between two moments, as one list: events
        /// and tasks, with repeating ones already unfolded into their dates.
        /// This is the call a month, week or day view makes.
        /// </summary>
        /// <param name="from">Start of the range (UTC).</param>
        /// <param name="to">End of the range (UTC), at most 366 days after the start.</param>
        /// <param name="calendarIds">Only events of these calendars. Left out: all visible calendars.</param>
        /// <param name="includeTasks">False leaves the tasks out.</param>
        [HttpGet("feed")]
        public async Task<IActionResult> GetFeed(
            [FromQuery] DateTime? from, [FromQuery] DateTime? to,
            [FromQuery] Guid[]? calendarIds = null, [FromQuery] bool includeTasks = true)
            => Ok(await _feed.GetFeedAsync(DateRange.Require(from, to, DateRange.MaxCalendarDays), calendarIds, includeTasks));

        // ── Calendars ─────────────────────────────────────────────────────────

        /// <summary>The calendars of the caller. Everyone has a default one ("Personal").</summary>
        [HttpGet]
        public async Task<IActionResult> GetMine() => Ok(await _calendars.GetMineAsync());

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SaveCalendarDto dto)
        {
            var created = await _calendars.CreateAsync(dto);
            return CreatedAtAction(nameof(GetMine), created);
        }

        /// <summary>Renames, recolours, hides or shows a calendar. Values left out stay as they are.</summary>
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] SaveCalendarDto dto)
            => OkOrNotFound(await _calendars.UpdateAsync(id, dto), "Calendar");

        /// <summary>Deletes a calendar. Its events move to the default calendar; the default one cannot be deleted.</summary>
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
            => NoContentOrNotFound(await _calendars.DeleteAsync(id), "Calendar");

        // ── Preferences ───────────────────────────────────────────────────────

        /// <summary>Week start, working hours, default view and the defaults for new events.</summary>
        [HttpGet("settings")]
        public async Task<IActionResult> GetSettings() => Ok(await _calendars.GetSettingsAsync());

        /// <summary>Replaces all calendar preferences.</summary>
        [HttpPut("settings")]
        public async Task<IActionResult> SaveSettings([FromBody] CalendarSettingsDto dto)
            => Ok(await _calendars.SaveSettingsAsync(dto));

        // ── Files ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The events of the caller as an .ics file, which Google Calendar,
        /// Outlook and Apple Calendar can import.
        /// </summary>
        /// <param name="calendarId">Only this calendar. Left out: all of them.</param>
        [HttpGet("export")]
        [Produces("text/calendar")]
        public async Task<IActionResult> Export([FromQuery] Guid? calendarId = null)
        {
            var ics = await _files.ExportAsync(calendarId);
            return File(Encoding.UTF8.GetBytes(ics), "text/calendar; charset=utf-8", "miqat.ics");
        }

        /// <summary>
        /// Adds the events of an .ics file (exported from Google Calendar,
        /// Outlook, Apple Calendar, ...) to a calendar of the caller. Importing
        /// the same file again updates the events instead of copying them.
        /// </summary>
        /// <param name="calendarId">The calendar to import into. Left out: the default calendar.</param>
        [HttpPost("import")]
        [RequestSizeLimit(CalendarFileService.MaxFileBytes + 64 * 1024)] // the file plus multipart overhead
        public async Task<IActionResult> Import(IFormFile file, [FromQuery] Guid? calendarId = null)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Choose an .ics file to import." });
            if (file.Length > CalendarFileService.MaxFileBytes)
                return BadRequest(new { message = "The file is too large (2 MB at most)." });

            await using var content = file.OpenReadStream();
            return Ok(await _files.ImportAsync(content, calendarId));
        }
    }
}
