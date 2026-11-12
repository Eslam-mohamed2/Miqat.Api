using System.Linq.Expressions;
using Miqat.Application.Common;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;
using Miqat.Domain.Specifications;

namespace Miqat.Application.Specifications.Events
{
    /// <summary>Live events by id, with everything a detail view shows: owner, calendar, project, the invited people and the changed dates.</summary>
    public class EventsWithDetailsSpec : BaseSpecification<CalendarEvent>
    {
        public EventsWithDetailsSpec(Guid id) : this(new[] { id })
        {
        }

        public EventsWithDetailsSpec(IReadOnlyCollection<Guid> ids)
            : base(e => ids.Contains(e.Id) && !e.IsDeleted)
        {
            AddInclude(e => e.Owner);
            AddInclude(e => e.Calendar);
            AddInclude(e => e.Group!);
            AddInclude("Attendees.User");
            AddInclude(e => e.Exceptions);
        }
    }

    /// <summary>
    /// The events that can touch a range, for some people: the ones they own,
    /// the ones they are invited to and did not decline, and (optionally) the
    /// ones of the given projects.
    /// <para>
    /// A one-off event qualifies when it overlaps the range. A repeating one is
    /// stored once, so it qualifies when its series started before the end of
    /// the range and had not ended before the start; its exact dates are worked
    /// out afterwards.
    /// </para>
    /// </summary>
    public class EventsInRangeSpec : BaseSpecification<CalendarEvent>
    {
        /// <param name="withAttendeeNames">
        /// Also loads who each invited person is. The calendar feed only counts
        /// them, so it leaves this off and saves a join.
        /// </param>
        public EventsInRangeSpec(
            IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<Guid> projectIds, DateRange range,
            bool withAttendeeNames = false)
            : base(Touching(userIds, projectIds, range.From, range.To))
        {
            AddInclude(e => e.Owner);
            AddInclude(e => e.Calendar);
            AddInclude(e => e.Group!);
            AddInclude(withAttendeeNames ? "Attendees.User" : "Attendees");
            AddInclude(e => e.Exceptions);
            AddOrderBy(e => e.StartsAt);
        }

        private static Expression<Func<CalendarEvent, bool>> Touching(
            IReadOnlyCollection<Guid> userIds, IReadOnlyCollection<Guid> projectIds, DateTime from, DateTime to)
        {
            // "Repeat until" means the whole of that day for the owner, wherever
            // they are; a day of slack covers every time zone.
            var seriesEndFloor = from.AddDays(-1);

            return e => !e.IsDeleted &&
                        (userIds.Contains(e.OwnerId) ||
                         e.Attendees.Any(a => userIds.Contains(a.UserId) && a.Status != AttendeeStatus.Declined) ||
                         (e.GroupId != null && projectIds.Contains(e.GroupId.Value))) &&
                        e.StartsAt < to &&
                        (e.Recurrence == RecurrencePattern.None
                            ? e.EndsAt > from
                            : e.RecurrenceEndDate == null || e.RecurrenceEndDate >= seriesEndFloor);
        }
    }

    /// <summary>
    /// Events the user can see (their own, the ones they are invited to, and
    /// those of their projects) whose title, location or description contains
    /// the lower-cased term. Newest first, limited in the database.
    /// </summary>
    public class EventSearchSpec : BaseSpecification<CalendarEvent>
    {
        public EventSearchSpec(Guid userId, List<Guid> visibleGroupIds, string lowerTerm, int take)
            : base(e => !e.IsDeleted &&
                        (e.OwnerId == userId ||
                         e.Attendees.Any(a => a.UserId == userId) ||
                         (e.GroupId != null && visibleGroupIds.Contains(e.GroupId.Value))) &&
                        (e.Title.ToLower().Contains(lowerTerm) ||
                         (e.Location != null && e.Location.ToLower().Contains(lowerTerm)) ||
                         (e.Description != null && e.Description.ToLower().Contains(lowerTerm))))
        {
            AddOrderByDescending(e => e.StartsAt);
            ApplyPaging(0, take);
        }
    }

    /// <summary>Every live event a user owns - all of them, or those of one calendar - for writing them to a file.</summary>
    public class OwnEventsSpec : BaseSpecification<CalendarEvent>
    {
        public OwnEventsSpec(Guid ownerId, Guid? calendarId)
            : base(e => e.OwnerId == ownerId && !e.IsDeleted && (calendarId == null || e.CalendarId == calendarId))
        {
            AddInclude(e => e.Exceptions);
            AddOrderBy(e => e.StartsAt);
        }
    }

    /// <summary>Events the user is invited to and has not answered yet, soonest first. Past one-off events are left out.</summary>
    public class PendingInvitationsSpec : BaseSpecification<CalendarEvent>
    {
        public PendingInvitationsSpec(Guid userId, DateTime now)
            : base(e => !e.IsDeleted &&
                        e.Attendees.Any(a => a.UserId == userId && a.Status == AttendeeStatus.Pending) &&
                        (e.EndsAt > now || e.Recurrence != RecurrencePattern.None))
        {
            AddInclude(e => e.Owner);
            AddInclude(e => e.Calendar);
            AddInclude(e => e.Group!);
            AddInclude("Attendees.User");
            AddOrderBy(e => e.StartsAt);
        }
    }
}
