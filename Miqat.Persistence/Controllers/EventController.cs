using Microsoft.AspNetCore.Mvc;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;

namespace Miqat.API.Controllers
{
    /// <summary>Events (meetings, appointments): the things on a calendar that have a start and an end.</summary>
    public class EventController : BaseApiController
    {
        private const int DefaultRangeDays = 30;

        private readonly IEventService _events;
        private readonly IEventAttendeeService _attendees;

        public EventController(IEventService events, IEventAttendeeService attendees)
        {
            _events = events;
            _attendees = attendees;
        }

        /// <summary>
        /// The events of the caller that happen in a range: their own, the ones
        /// they are invited to and those of their projects. A repeating event
        /// is listed once here; GET /api/Calendar/feed lists each of its dates.
        /// </summary>
        /// <param name="from">Start of the range (UTC). Left out: now.</param>
        /// <param name="to">End of the range (UTC). Left out: 30 days after the start.</param>
        /// <param name="calendarId">Only events of this calendar.</param>
        [HttpGet]
        public async Task<IActionResult> GetInRange(
            [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null, [FromQuery] Guid? calendarId = null)
        {
            var start = from ?? DateTime.UtcNow;
            var range = DateRange.Require(start, to ?? start.AddDays(DefaultRangeDays), DateRange.MaxCalendarDays);

            return Ok(await _events.GetInRangeAsync(range, calendarId));
        }

        /// <summary>Events the caller is invited to and has not answered yet.</summary>
        [HttpGet("invitations")]
        public async Task<IActionResult> GetInvitations() => Ok(await _attendees.GetInvitationsAsync());

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
            => OkOrNotFound(await _events.GetByIdAsync(id), "Event");

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SaveEventDto dto)
        {
            var created = await _events.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }

        /// <summary>Replaces the event. Changing its time or how it repeats resets changes made to single dates.</summary>
        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] SaveEventDto dto)
            => OkOrNotFound(await _events.UpdateAsync(id, dto), "Event");

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
            => NoContentOrNotFound(await _events.DeleteAsync(id), "Event");

        // ── Single dates of a repeating event ─────────────────────────────────

        /// <summary>
        /// Changes or skips one date of a repeating event and leaves the rest of
        /// the series alone. "occurrenceStart" is the value the calendar feed
        /// gave that date.
        /// </summary>
        [HttpPut("{id:guid}/occurrences")]
        public async Task<IActionResult> SaveOccurrence(Guid id, [FromBody] SaveOccurrenceDto dto)
            => OkOrNotFound(await _events.SaveOccurrenceAsync(id, dto), "Event");

        /// <summary>Puts one date back to what the series says (undoes a change or a skip).</summary>
        [HttpDelete("{id:guid}/occurrences")]
        public async Task<IActionResult> RestoreOccurrence(Guid id, [FromQuery] DateTime occurrenceStart)
        {
            var restored = await _events.RestoreOccurrenceAsync(id, UtcDateTimeConverter.Normalize(occurrenceStart)!.Value);
            return NoContentOrNotFound(restored, "A change on that date was");
        }

        // ── Invitations ───────────────────────────────────────────────────────

        /// <summary>Invites people (friends and teammates of the owner). Owner only.</summary>
        [HttpPost("{id:guid}/attendees")]
        public async Task<IActionResult> Invite(Guid id, [FromBody] InviteAttendeesDto dto)
            => OkOrNotFound(await _attendees.InviteAsync(id, dto.UserIds), "Event");

        /// <summary>The owner removes an invited person, or an invited person removes themselves.</summary>
        [HttpDelete("{id:guid}/attendees/{userId:guid}")]
        public async Task<IActionResult> RemoveAttendee(Guid id, Guid userId)
            => NoContentOrNotFound(await _attendees.RemoveAsync(id, userId), "Invitation");

        /// <summary>The answer of the caller to an invitation: Accepted, Tentative or Declined.</summary>
        [HttpPut("{id:guid}/respond")]
        public async Task<IActionResult> Respond(Guid id, [FromBody] RespondToEventDto dto)
            => OkOrNotFound(await _attendees.RespondAsync(id, dto.Status), "Event");
    }
}
