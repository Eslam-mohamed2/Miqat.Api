using Miqat.Application.Common;
using Miqat.Application.Modules;

namespace Miqat.Application.Interfaces
{
    /// <summary>Finding a time that suits several people.</summary>
    public interface IScheduleService
    {
        /// <summary>
        /// When each person is busy inside the range. Only for the caller and
        /// the people they are connected to (friends and teammates).
        /// </summary>
        Task<List<UserAvailabilityDto>> GetAvailabilityAsync(IReadOnlyCollection<Guid> userIds, DateRange range);

        /// <summary>The earliest times at which everyone is free, at most a few per day.</summary>
        Task<List<TimeSlotDto>> SuggestAsync(SuggestTimesDto request);
    }
}
