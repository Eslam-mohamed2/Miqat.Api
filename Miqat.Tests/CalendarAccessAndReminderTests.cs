using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Services;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Tests;

/// <summary>
/// Who may see and change an event, and what the reminder job sends.
/// </summary>
public class CalendarAccessAndReminderTests
{
    private static readonly User Owner = Person("Owner");
    private static readonly Guid Guest = Guid.NewGuid();
    private static readonly Guid Teammate = Guid.NewGuid();
    private static readonly Guid Outsider = Guid.NewGuid();

    private static User Person(string name) =>
        new(name, name.ToLowerInvariant() + "@example.com", DateTime.UtcNow.AddYears(-30), null, null);

    private static CalendarEvent NewEvent(DateTime start, Guid? groupId = null, RecurrencePattern repeat = RecurrencePattern.None)
    {
        var calendarEvent = new CalendarEvent("Planning", start, start.AddHours(1), Owner, Guid.NewGuid()) { GroupId = groupId };
        calendarEvent.Repeat(repeat);
        return calendarEvent;
    }

    private static FakeRepository<T> Repo<T>(Func<T, Guid> idOf, params T[] items) where T : class =>
        new(items.ToList(), idOf);

    // ── Access ───────────────────────────────────────────────────────────────

    private static (AccessPolicy Policy, CalendarEvent Event) PolicyFor(Guid? caller, string role = "User", bool inProject = false)
    {
        var project = new Group("Website", "desc", Owner.Id, "#2ec4a0");
        var calendarEvent = NewEvent(DateTime.UtcNow.AddDays(1), inProject ? project.Id : null);

        var uow = new FakeUnitOfWork();
        uow.Register(Repo(e => e.Id, calendarEvent));
        uow.Register(Repo(a => a.Id, new EventAttendee(calendarEvent.Id, Guest)));
        uow.Register(Repo(g => g.Id, project));
        uow.Register(Repo(m => m.Id, new GroupMember(project.Id, Teammate)));
        uow.Register(Repo<TaskItem>(t => t.Id));

        return (new AccessPolicy(uow, new FakeCurrentUser(caller, role)), calendarEvent);
    }

    [Fact]
    public async Task The_owner_sees_and_changes_their_event()
    {
        var (policy, calendarEvent) = PolicyFor(Owner.Id);

        Assert.True(await policy.CanViewEventAsync(calendarEvent.Id));
        Assert.True(await policy.CanManageEventAsync(calendarEvent.Id));
    }

    [Fact]
    public async Task An_invited_person_sees_the_event_but_cannot_change_it()
    {
        var (policy, calendarEvent) = PolicyFor(Guest);

        Assert.True(await policy.CanViewEventAsync(calendarEvent.Id));
        Assert.False(await policy.CanManageEventAsync(calendarEvent.Id));
    }

    [Fact]
    public async Task A_project_event_is_visible_to_the_project_and_to_nobody_else()
    {
        var (asTeammate, projectEvent) = PolicyFor(Teammate, inProject: true);
        Assert.True(await asTeammate.CanViewEventAsync(projectEvent.Id));

        var (stillATeammate, privateEvent) = PolicyFor(Teammate, inProject: false);
        Assert.False(await stillATeammate.CanViewEventAsync(privateEvent.Id));

        var (asOutsider, anyEvent) = PolicyFor(Outsider, inProject: true);
        Assert.False(await asOutsider.CanViewEventAsync(anyEvent.Id));
    }

    [Fact]
    public async Task A_deleted_event_is_gone_for_everyone_admins_included()
    {
        var (policy, calendarEvent) = PolicyFor(Guid.NewGuid(), role: "Admin");
        Assert.True(await policy.CanManageEventAsync(calendarEvent.Id));

        calendarEvent.SoftDelete();

        Assert.False(await policy.CanViewEventAsync(calendarEvent.Id));
        Assert.False(await policy.CanManageEventAsync(calendarEvent.Id));
    }

    [Fact]
    public async Task Things_on_an_event_answer_404_when_it_is_missing_and_403_when_it_is_not_yours()
    {
        var (asOutsider, calendarEvent) = PolicyFor(Outsider);
        var (asGuest, sameKindOfEvent) = PolicyFor(Guest);

        var missing = await Assert.ThrowsAsync<ApiException>(
            () => asOutsider.RequireItemAsync(CalendarItemKind.Event, Guid.NewGuid()));
        Assert.Equal(404, missing.StatusCode);

        var notYours = await Assert.ThrowsAsync<ApiException>(
            () => asOutsider.RequireItemAsync(CalendarItemKind.Event, calendarEvent.Id));
        Assert.Equal(403, notYours.StatusCode);

        // A guest may look (set their own reminder), but not change the event.
        await asGuest.RequireItemAsync(CalendarItemKind.Event, sameKindOfEvent.Id);
        var readOnly = await Assert.ThrowsAsync<ApiException>(
            () => asGuest.RequireItemAsync(CalendarItemKind.Event, sameKindOfEvent.Id, toChange: true));
        Assert.Equal(403, readOnly.StatusCode);
    }

    // ── Reminders ────────────────────────────────────────────────────────────

    /// <summary>Records what would have been sent instead of sending it.</summary>
    private sealed class RecordingSender : INotificationSender
    {
        public List<(NotificationType Type, string Message, Guid Recipient)> Sent { get; } = new();

        public Task<StagedNotifications> StageAsync(NotificationContent content, IEnumerable<Guid> recipientIds)
        {
            var message = content.Message("Someone");
            var items = recipientIds.Distinct()
                .Select(id => new Notification(content.Title, message, content.Type, id))
                .ToList();
            Sent.AddRange(items.Select(n => (n.Type, n.Message, n.RecipientUserId)));

            return Task.FromResult(new StagedNotifications(items, null));
        }

        public Task DeliverAsync(StagedNotifications staged) => Task.CompletedTask;
        public Task SendAsync(NotificationContent content, IEnumerable<Guid> recipientIds) => StageAsync(content, recipientIds);
    }

    private static (ReminderDispatcher Dispatcher, RecordingSender Sender, List<Reminder> Reminders) DispatcherFor(
        IEnumerable<Reminder> reminders, IEnumerable<TaskItem>? tasks = null, IEnumerable<CalendarEvent>? events = null)
    {
        var reminderList = reminders.ToList();

        var uow = new FakeUnitOfWork();
        uow.Register(new FakeRepository<Reminder>(reminderList, r => r.Id));
        uow.Register(new FakeRepository<TaskItem>((tasks ?? Array.Empty<TaskItem>()).ToList(), t => t.Id));
        uow.Register(new FakeRepository<CalendarEvent>((events ?? Array.Empty<CalendarEvent>()).ToList(), e => e.Id));

        var sender = new RecordingSender();
        return (new ReminderDispatcher(uow, sender), sender, reminderList);
    }

    private static readonly TimeSpan OneDay = TimeSpan.FromHours(24);

    [Fact]
    public async Task A_reminder_on_a_weekly_event_fires_once_and_then_waits_for_next_week()
    {
        var now = DateTime.UtcNow;
        var start = now.AddMinutes(10);
        var weekly = NewEvent(start, repeat: RecurrencePattern.Weekly);

        var reminder = new Reminder(Guest, CalendarItemKind.Event, weekly.Id, minutesBefore: 10);
        reminder.ScheduleFor(start);

        var (dispatcher, sender, _) = DispatcherFor(new[] { reminder }, events: new[] { weekly });

        Assert.Equal(1, await dispatcher.RunAsync(now, OneDay));
        Assert.Equal((NotificationType.Reminder, "\"Planning\" starts in 10 minutes.", Guest), sender.Sent.Single());

        Assert.Equal(start.AddDays(7), reminder.ItemTime);
        Assert.Equal(start.AddDays(7).AddMinutes(-10), reminder.RemindAt);

        // The very next pass has nothing to say.
        Assert.Equal(0, await dispatcher.RunAsync(now.AddMinutes(1), OneDay));
    }

    [Fact]
    public async Task A_snoozed_reminder_fires_again_about_the_same_date()
    {
        var now = DateTime.UtcNow;
        var start = now.AddMinutes(5);
        var meeting = NewEvent(start);

        // Fired at "10 minutes before", then snoozed for five.
        var reminder = new Reminder(Guest, CalendarItemKind.Event, meeting.Id, minutesBefore: 10);
        reminder.ScheduleFor(start);
        reminder.MarkSent(now.AddMinutes(-5), nextItemTime: null);
        reminder.SnoozeUntil(now);

        var (dispatcher, sender, _) = DispatcherFor(new[] { reminder }, events: new[] { meeting });

        Assert.Equal(1, await dispatcher.RunAsync(now, OneDay));
        Assert.Equal("\"Planning\" starts in 5 minutes.", sender.Sent.Single().Message);
        Assert.Null(reminder.RemindAt);
    }

    [Fact]
    public async Task A_reminder_on_a_finished_task_says_nothing_and_one_on_a_deleted_task_is_removed()
    {
        var now = DateTime.UtcNow;

        var finished = new TaskItem("Done already", null, Owner.Id, dueDate: now.AddMinutes(20));
        finished.ChangeStatus(Domain.Enumerations.TaskStatus.Completed);
        var deleted = new TaskItem("Thrown away", null, Owner.Id, dueDate: now.AddMinutes(20));
        deleted.SoftDelete();

        var onFinished = new Reminder(Owner.Id, CalendarItemKind.Task, finished.Id, minutesBefore: 30);
        onFinished.ScheduleFor(finished.DueDate);
        var onDeleted = new Reminder(Owner.Id, CalendarItemKind.Task, deleted.Id, minutesBefore: 30);
        onDeleted.ScheduleFor(deleted.DueDate);

        var (dispatcher, sender, reminders) = DispatcherFor(new[] { onFinished, onDeleted }, tasks: new[] { finished, deleted });

        Assert.Equal(0, await dispatcher.RunAsync(now, OneDay));
        Assert.Empty(sender.Sent);
        Assert.Equal(new[] { onFinished }, reminders);
        Assert.Null(onFinished.RemindAt);
    }

    [Fact]
    public async Task The_due_soon_notice_goes_to_owner_and_assignee_once_and_again_only_when_the_date_moves()
    {
        var now = DateTime.UtcNow;
        var task = new TaskItem("Send the report", null, Owner.Id, dueDate: now.AddHours(5), assignedToUserId: Guest);

        var (dispatcher, sender, _) = DispatcherFor(Array.Empty<Reminder>(), tasks: new[] { task });

        Assert.Equal(2, await dispatcher.RunAsync(now, OneDay));
        Assert.All(sender.Sent, n => Assert.Equal(NotificationType.TaskDueSoon, n.Type));
        Assert.Equal(new[] { Owner.Id, Guest }.OrderBy(id => id), sender.Sent.Select(n => n.Recipient).OrderBy(id => id));
        Assert.Equal("\"Send the report\" is due in about 5 hours.", sender.Sent.First().Message);

        Assert.Equal(0, await dispatcher.RunAsync(now.AddMinutes(1), OneDay));

        task.DueDate = now.AddHours(8);
        Assert.Equal(2, await dispatcher.RunAsync(now.AddMinutes(2), OneDay));
    }
}
