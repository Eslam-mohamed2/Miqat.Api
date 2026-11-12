using Microsoft.AspNetCore.Mvc;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Services;

namespace Miqat.API.Controllers
{
    /// <summary>Finding a time that suits several people.</summary>
    public class ScheduleController : BaseApiController
    {
        private readonly IScheduleService _schedule;

        public ScheduleController(IScheduleService schedule) => _schedule = schedule;

        /// <summary>
        /// When people are busy between two moments. Only times are returned,
        /// never what the events are, and only for the caller, their friends
        /// and the people on their projects.
        /// </summary>
        /// <param name="userIds">Whose free time to look at. Left out: the caller.</param>
        /// <param name="from">Start of the range (UTC).</param>
        /// <param name="to">End of the range (UTC), at most 31 days after the start.</param>
        [HttpGet("availability")]
        public async Task<IActionResult> GetAvailability(
            [FromQuery] Guid[]? userIds, [FromQuery] DateTime? from, [FromQuery] DateTime? to)
        {
            var range = DateRange.Require(from, to, ScheduleService.MaxRangeDays);
            return Ok(await _schedule.GetAvailabilityAsync(userIds ?? Array.Empty<Guid>(), range));
        }

        /// <summary>
        /// Suggests times at which the caller and the given people are all free,
        /// inside the working hours of each of them (their calendar settings and
        /// time zone). The earliest times come first, a few per day.
        /// </summary>
        [HttpPost("suggest")]
        public async Task<IActionResult> Suggest([FromBody] SuggestTimesDto request)
            => Ok(await _schedule.SuggestAsync(request));
    }
}
