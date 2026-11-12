using Microsoft.EntityFrameworkCore;
using Miqat.Application.Common;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.infrastructure.persistence.Data.Seeds
{
    /// <summary>
    /// Gives every demo account a small calendar to look at: two calendars, a
    /// weekly meeting with invited friends, an appointment, an all-day event, a
    /// reminder, and a checklist and a dependency on their tasks.
    /// <para>
    /// Runs on every start-up while demo data is on, and only touches accounts
    /// that have no calendar yet - so it also fills in databases that were
    /// seeded before the calendar existed, and never adds anything twice.
    /// </para>
    /// </summary>
    public class CalendarDemoSeeder
    {
        private const int MaxGuests = 6;

        private readonly MiqatDbContext _context;

        public CalendarDemoSeeder(MiqatDbContext context) => _context = context;

        public async Task SeedAsync()
        {
            // Accounts that can actually sign in and have nothing on their calendar.
            var users = await _context.Users
                .Where(u => !u.IsDeleted && u.IsActive && u.IsVerified &&
                            u.PasswordHash != null && u.PasswordHash != "" &&
                            !_context.Calendars.Any(c => c.OwnerId == u.Id))
                .ToListAsync();
            if (users.Count == 0) return;

            var userIds = users.Select(u => u.Id).ToList();
            var friendships = await _context.Friendships
                .Where(f => f.Status == FriendshipStatus.Accepted && !f.IsDeleted &&
                            (userIds.Contains(f.SenderId) || userIds.Contains(f.ReceiverId)))
                .ToListAsync();
            var tasks = await _context.Tasks
                .Where(t => userIds.Contains(t.UserId) && !t.IsDeleted &&
                            t.Status != Domain.Enumerations.TaskStatus.Completed &&
                            t.Status != Domain.Enumerations.TaskStatus.Cancelled)
                .ToListAsync();

            var now = DateTime.UtcNow;

            foreach (var user in users)
            {
                var zone = TimeZones.Resolve(user.TimeZone);
                var today = TimeZones.ToLocal(now, zone).Date;
                DateTime At(int daysAhead, int hour, int minute = 0) =>
                    TimeZones.ToUtc(today.AddDays(daysAhead).AddHours(hour).AddMinutes(minute), zone);

                var personal = UserCalendar.CreateDefault(user.Id);
                var work = new UserCalendar("Work", "#F97316", user.Id);
                _context.Calendars.AddRange(personal, work);

                // Next Monday at 10:00 on the clock of the user, every week.
                var untilMonday = ((int)DayOfWeek.Monday - (int)today.DayOfWeek + 7) % 7;
                var standUp = new CalendarEvent(
                    "Team stand-up", At(untilMonday == 0 ? 7 : untilMonday, 10), At(untilMonday == 0 ? 7 : untilMonday, 10, 30),
                    user, work.Id)
                {
                    Description = "What did you finish, what is next, is anything in the way?",
                    Location = "Online"
                };
                standUp.Repeat(RecurrencePattern.Weekly);

                var appointment = new CalendarEvent("Dentist appointment", At(3, 15), At(3, 16), user, personal.Id)
                {
                    Location = "City Dental Clinic"
                };
                var dayOff = new CalendarEvent("Day off", At(10, 0), At(11, 0), user, personal.Id, isAllDay: true);
                _context.Events.AddRange(standUp, appointment, dayOff);

                // Friends are invited to the stand-up; every second one has already
                // said yes, the others still have an invitation to answer.
                var friendIds = friendships
                    .Where(f => f.SenderId == user.Id || f.ReceiverId == user.Id)
                    .Select(f => f.SenderId == user.Id ? f.ReceiverId : f.SenderId)
                    .Distinct().OrderBy(id => id).Take(MaxGuests).ToList();

                for (var i = 0; i < friendIds.Count; i++)
                {
                    var attendee = new EventAttendee(standUp.Id, friendIds[i]);
                    if (i % 2 == 1) attendee.Respond(AttendeeStatus.Accepted);
                    _context.EventAttendees.Add(attendee);
                }

                var reminder = new Reminder(user.Id, CalendarItemKind.Event, standUp.Id, minutesBefore: 15);
                reminder.ScheduleFor(standUp.StartsAt);
                _context.Reminders.Add(reminder);

                // A checklist on their first open task, which waits for their second.
                var mine = tasks.Where(t => t.UserId == user.Id).OrderBy(t => t.CreatedAt).Take(2).ToList();
                if (mine.Count > 0)
                {
                    var first = new Subtask(mine[0].Id, "Collect what is needed", 0);
                    first.SetDone(true);
                    _context.Subtasks.AddRange(
                        first,
                        new Subtask(mine[0].Id, "Do the work", 1),
                        new Subtask(mine[0].Id, "Ask for a review", 2));
                }
                if (mine.Count == 2)
                    _context.TaskDependencies.Add(new TaskDependency(mine[0].Id, mine[1].Id));
            }

            await _context.SaveChangesAsync();
            Console.WriteLine($"[CalendarSeed] ✅ Calendar demo data added for {users.Count} account(s).");
        }
    }
}
