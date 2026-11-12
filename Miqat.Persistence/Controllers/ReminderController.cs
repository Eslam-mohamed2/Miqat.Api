using Microsoft.AspNetCore.Mvc;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Domain.Enumerations;

namespace Miqat.API.Controllers
{
    /// <summary>
    /// "Remind me N minutes before", on a task or an event. The same two
    /// addresses exist for both: /api/Task/{id}/reminders and
    /// /api/Event/{id}/reminders. Reminders are personal: everyone sets and
    /// sees their own.
    /// </summary>
    [Route("api")]
    public class ReminderController : BaseApiController
    {
        private const string OnItem = "{itemKind:" + ItemKindRouteConstraint.Name + "}/{itemId:guid}/reminders";

        private readonly IReminderService _reminders;

        public ReminderController(IReminderService reminders) => _reminders = reminders;

        /// <summary>All reminders of the caller that still have something ahead, soonest first.</summary>
        [HttpGet("Reminder")]
        public async Task<IActionResult> GetUpcoming() => Ok(await _reminders.GetUpcomingAsync());

        /// <summary>The reminders of the caller on one task or event.</summary>
        /// <param name="itemKind">Task or Event.</param>
        [HttpGet(OnItem)]
        public async Task<IActionResult> GetForItem(CalendarItemKind itemKind, Guid itemId)
            => Ok(await _reminders.GetForItemAsync(itemKind, itemId));

        /// <summary>Adds a reminder. For a repeating event it fires before every date.</summary>
        /// <param name="itemKind">Task or Event.</param>
        [HttpPost(OnItem)]
        public async Task<IActionResult> Add(CalendarItemKind itemKind, Guid itemId, [FromBody] SaveReminderDto dto)
        {
            var reminder = await _reminders.AddAsync(itemKind, itemId, dto.MinutesBefore);
            return CreatedAtAction(nameof(GetForItem), new { itemKind, itemId }, reminder);
        }

        [HttpDelete("Reminder/{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
            => NoContentOrNotFound(await _reminders.DeleteAsync(id), "Reminder");

        /// <summary>"Remind me again in N minutes."</summary>
        [HttpPost("Reminder/{id:guid}/snooze")]
        public async Task<IActionResult> Snooze(Guid id, [FromBody] SnoozeReminderDto dto)
            => OkOrNotFound(await _reminders.SnoozeAsync(id, dto.Minutes), "Reminder");
    }
}
