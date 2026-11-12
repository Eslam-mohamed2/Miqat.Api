using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Miqat.Application.Common;
using Miqat.Application.Scheduling;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;
using Miqat.infrastructure.persistence.Data;

namespace Miqat.API.Swagger;

/// <summary>A person as the profile forms see them.</summary>
public sealed record SamplePerson(
    string FullName,
    string Email,
    string? PhoneNumber,
    string? Country,
    string TimeZone,
    string? Gender,
    DateTime? DateOfBirth,
    string? Bio);

/// <summary>
/// The values the Swagger examples are built from.
///
/// With demo data switched on these are real rows that belong to the demo
/// account, so pressing "Execute" works without typing anything. An id that is
/// null means the demo account has no such row right now (for example no
/// board yet); the example then shows a made-up id and says so.
/// </summary>
public sealed class SwaggerSamples
{
    /// <summary>True when the values below were read from the database.</summary>
    public bool IsLive { get; init; }

    public string Email { get; init; } = "user@example.com";
    public string Password { get; init; } = "Passw0rd!";

    public SamplePerson? Me { get; init; }

    /// <summary>Somebody the account already knows: a friend if there is one.</summary>
    public Guid? OtherUserId { get; init; }
    public SamplePerson? OtherUser { get; init; }

    /// <summary>Somebody with no friendship row to the account, in any state.</summary>
    public Guid? StrangerUserId { get; init; }
    public Guid? BlockedUserId { get; init; }

    public Guid? FriendshipId { get; init; }
    public Guid? PendingFriendshipId { get; init; }

    public Guid? GroupId { get; init; }
    public Guid? GroupMemberUserId { get; init; }
    public Guid? NonMemberUserId { get; init; }

    public Guid? TaskId { get; init; }
    public Guid? CommentId { get; init; }
    public Guid? NotificationId { get; init; }
    public Guid? MentionId { get; init; }
    public Guid? BoardId { get; init; }

    // ── Calendar ────────────────────────────────────────────────────────────

    /// <summary>A calendar of the account that is not its default one (so it can be renamed and deleted).</summary>
    public Guid? CalendarId { get; init; }

    /// <summary>An event of the account that does not repeat.</summary>
    public Guid? EventId { get; init; }

    /// <summary>A repeating event of the account, the next date of its series and one person invited to it.</summary>
    public Guid? RepeatingEventId { get; init; }
    public DateTime? OccurrenceStart { get; init; }
    public Guid? AttendeeUserId { get; init; }

    /// <summary>An event somebody else invited the account to; an unanswered one when there is one.</summary>
    public Guid? InvitedEventId { get; init; }

    /// <summary>A friend or teammate: someone the account may invite and whose free time it may see.</summary>
    public Guid? ContactUserId { get; init; }

    public Guid? ReminderId { get; init; }
    public Guid? AttachmentId { get; init; }
    public Guid? DeviceId { get; init; }

    // ── Checklist and links between tasks ───────────────────────────────────
    public Guid? SubtaskId { get; init; }

    /// <summary>An existing link: the first task waits for the second.</summary>
    public Guid? WaitingTaskId { get; init; }
    public Guid? DependsOnTaskId { get; init; }

    /// <summary>A task of the account with no links at all, safe to link to.</summary>
    public Guid? UnlinkedTaskId { get; init; }

    // ── Chat ────────────────────────────────────────────────────────────────
    /// <summary>A chat of the account, preferring one with a friend it can still write to.</summary>
    public Guid? ConversationId { get; init; }

    /// <summary>A message the account sent in that chat (only the sender may edit or delete it).</summary>
    public Guid? ChatMessageId { get; init; }

    /// <summary>A word that occurs in one of the account's task titles.</summary>
    public string SearchWord { get; init; } = "report";

    public static SwaggerSamples Placeholder() => new();
}

/// <summary>
/// Looks the sample values up, once per generated Swagger document.
///
/// Real ids are only ever read when Seed:DemoData is on. Swagger can be
/// enabled on a deployment that holds real accounts, and the document is
/// served to anyone who can open the page, so on such a deployment nothing
/// from the database may end up in it.
/// </summary>
public sealed class SwaggerSampleProvider
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SwaggerSampleProvider> _logger;

    public SwaggerSampleProvider(
        IServiceScopeFactory scopes,
        IConfiguration configuration,
        ILogger<SwaggerSampleProvider> logger)
    {
        _scopes = scopes;
        _configuration = configuration;
        _logger = logger;
    }

    public SwaggerSamples Resolve()
    {
        if (!_configuration.GetValue<bool>("Seed:DemoData"))
            return SwaggerSamples.Placeholder();

        try
        {
            var account = ReadDemoAccount();
            if (account == null) return SwaggerSamples.Placeholder();

            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MiqatDbContext>();
            return Load(db, account.Value.Email, account.Value.Password);
        }
        catch (Exception ex)
        {
            // Examples are a convenience: the Swagger page must still load.
            _logger.LogWarning(ex, "Swagger examples: falling back to placeholder values.");
            return SwaggerSamples.Placeholder();
        }
    }

    /// <summary>
    /// The demo account to sign in with: the first seeded admin, so the
    /// admin-only endpoints can be tried too. Its password is already in the
    /// repository (SeedData/users.json); nothing new is exposed here.
    /// </summary>
    private static (string Email, string Password)? ReadDemoAccount()
    {
        var file = Path.Combine(AppContext.BaseDirectory, "SeedData", "users.json");
        if (!File.Exists(file)) return null;

        using var json = JsonDocument.Parse(File.ReadAllText(file));
        (string Email, string Password)? firstUsable = null;

        foreach (var user in json.RootElement.EnumerateArray())
        {
            if (!Flag(user, "isActive") || !Flag(user, "isVerified")) continue;

            var email = Text(user, "email");
            var password = Text(user, "passwordPlain");
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password)) continue;

            if (string.Equals(Text(user, "role"), "Admin", StringComparison.OrdinalIgnoreCase))
                return (email!, password!);

            firstUsable ??= (email!, password!);
        }

        return firstUsable;

        static string? Text(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        static bool Flag(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
    }

    private static SwaggerSamples Load(MiqatDbContext db, string email, string password)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();

        var me = db.Users.AsNoTracking()
            .FirstOrDefault(u => u.Email.ToLower() == normalizedEmail && !u.IsDeleted && u.IsActive);
        if (me == null) return SwaggerSamples.Placeholder();

        var myId = me.Id;

        // ── People ──────────────────────────────────────────────────────────
        var friendships = db.Friendships.AsNoTracking()
            .Where(f => (f.SenderId == myId || f.ReceiverId == myId) && !f.IsDeleted)
            .OrderBy(f => f.CreatedAt).ThenBy(f => f.Id)
            .Select(f => new { f.Id, f.SenderId, f.ReceiverId, f.Status })
            .ToList();

        var relatedIds = friendships
            .Select(f => f.SenderId == myId ? f.ReceiverId : f.SenderId)
            .Distinct()
            .ToList();

        var friendIds = friendships
            .Where(f => f.Status == FriendshipStatus.Accepted)
            .Select(f => f.SenderId == myId ? f.ReceiverId : f.SenderId)
            .ToList();

        var liveUsers = db.Users.AsNoTracking()
            .Where(u => u.Id != myId && !u.IsDeleted && u.IsActive);

        var other = liveUsers
                        .Where(u => friendIds.Contains(u.Id))
                        .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
                        .FirstOrDefault()
                    ?? liveUsers.OrderBy(u => u.CreatedAt).ThenBy(u => u.Id).FirstOrDefault();

        var strangerId = liveUsers
            .Where(u => !relatedIds.Contains(u.Id) && u.Role != UserRole.Admin)
            .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefault();

        // A Blocked row always stores the blocker as the sender.
        var blockedId = friendships
            .Where(f => f.Status == FriendshipStatus.Blocked && f.SenderId == myId)
            .Select(f => (Guid?)f.ReceiverId)
            .FirstOrDefault();

        var friendshipId =
            friendships.Where(f => f.Status == FriendshipStatus.Accepted).Select(f => (Guid?)f.Id).FirstOrDefault()
            ?? friendships.Select(f => (Guid?)f.Id).FirstOrDefault();

        var pendingFriendshipId = friendships
            .Where(f => f.Status == FriendshipStatus.Pending && f.ReceiverId == myId)
            .Select(f => (Guid?)f.Id)
            .FirstOrDefault();

        // ── Project: one this account owns, preferring one that has members ──
        var groupId = db.Groups.AsNoTracking()
            .Where(g => g.OwnerId == myId && !g.IsDeleted)
            .OrderByDescending(g => g.Members.Count(m => !m.IsDeleted && m.UserId != myId))
            .ThenBy(g => g.CreatedAt).ThenBy(g => g.Id)
            .Select(g => (Guid?)g.Id)
            .FirstOrDefault();

        Guid? groupMemberUserId = null;
        Guid? nonMemberUserId = null;

        if (groupId.HasValue)
        {
            var memberIds = db.GroupMembers.AsNoTracking()
                .Where(m => m.GroupId == groupId.Value && !m.IsDeleted)
                .OrderBy(m => m.JoinedAt).ThenBy(m => m.Id)
                .Select(m => m.UserId)
                .ToList();

            groupMemberUserId = liveUsers
                .Where(u => memberIds.Contains(u.Id))
                .OrderBy(u => u.CreatedAt).ThenBy(u => u.Id)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefault();

            nonMemberUserId = liveUsers
                .Where(u => !memberIds.Contains(u.Id))
                .OrderByDescending(u => friendIds.Contains(u.Id))
                .ThenBy(u => u.CreatedAt).ThenBy(u => u.Id)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefault();
        }

        // ── Task: one of the account's own, preferring one inside that project ─
        var task = db.Tasks.AsNoTracking()
            .Where(t => t.UserId == myId && !t.IsDeleted)
            .OrderByDescending(t => groupId.HasValue && t.GroupId == groupId)
            .ThenBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .Select(t => new { t.Id, t.Title })
            .FirstOrDefault();

        var taskId = task?.Id;

        var commentId = db.Comments.AsNoTracking()
            .Where(c => c.AuthorId == myId && !c.IsDeleted)
            .OrderByDescending(c => taskId.HasValue && c.TaskId == taskId)
            .ThenByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefault();

        var notificationId = db.Notifications.AsNoTracking()
            .Where(n => n.RecipientUserId == myId && !n.IsDeleted)
            .OrderBy(n => n.IsRead).ThenByDescending(n => n.CreatedAt).ThenBy(n => n.Id)
            .Select(n => (Guid?)n.Id)
            .FirstOrDefault();

        var mentionId = db.Mentions.AsNoTracking()
            .Where(m => m.MentionedUserId == myId && !m.IsDeleted)
            .OrderBy(m => m.IsRead).ThenByDescending(m => m.CreatedAt).ThenBy(m => m.Id)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefault();

        var boardId = db.Boards.AsNoTracking()
            .Where(b => b.OwnerId == myId && !b.IsDeleted)
            .OrderByDescending(b => b.CreatedAt).ThenBy(b => b.Id)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefault();

        // ── Calendar ────────────────────────────────────────────────────────
        var calendarId = db.Calendars.AsNoTracking()
            .Where(c => c.OwnerId == myId && !c.IsDefault)
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefault();

        var myEvents = db.Events.AsNoTracking()
            .Where(e => e.OwnerId == myId && !e.IsDeleted)
            .OrderBy(e => e.CreatedAt).ThenBy(e => e.Id)
            .Select(e => new
            {
                e.Id,
                e.StartsAt,
                e.Recurrence,
                e.RecurrenceInterval,
                e.RecurrenceEndDate,
                AttendeeUserId = e.Attendees.OrderBy(a => a.CreatedAt).Select(a => (Guid?)a.UserId).FirstOrDefault()
            })
            .ToList();

        var plainEvent = myEvents.FirstOrDefault(e => e.Recurrence == RecurrencePattern.None);
        var repeatingEvent = myEvents
            .Where(e => e.Recurrence != RecurrencePattern.None)
            .OrderByDescending(e => e.AttendeeUserId.HasValue)
            .FirstOrDefault();

        // Taken from the series itself, not from what is left of it, so it
        // stays the same date after the example changes or skips that date.
        var occurrenceStart = repeatingEvent == null
            ? null
            : Recurrence.NextStart(
                repeatingEvent.StartsAt,
                new RecurrenceRule(repeatingEvent.Recurrence, repeatingEvent.RecurrenceInterval, repeatingEvent.RecurrenceEndDate),
                TimeZones.Resolve(me.TimeZone),
                DateTime.UtcNow);

        // An invitation that still waits for an answer if there is one; an
        // answer can also be changed, so an answered one will do.
        var invitedEventId = db.Events.AsNoTracking()
            .Where(e => !e.IsDeleted && e.Attendees.Any(a => a.UserId == myId))
            .OrderByDescending(e => e.Attendees.Any(a => a.UserId == myId && a.Status == AttendeeStatus.Pending))
            .ThenBy(e => e.StartsAt).ThenBy(e => e.Id)
            .Select(e => (Guid?)e.Id)
            .FirstOrDefault();

        var reminderId = db.Reminders.AsNoTracking()
            .Where(r => r.UserId == myId)
            .OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefault();

        var attachmentId = db.Attachments.AsNoTracking()
            .Where(a => a.UploadedByUserId == myId)
            .OrderByDescending(a => a.CreatedAt).ThenBy(a => a.Id)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefault();

        var deviceId = db.Devices.AsNoTracking()
            .Where(d => d.UserId == myId)
            .OrderByDescending(d => d.CreatedAt).ThenBy(d => d.Id)
            .Select(d => (Guid?)d.Id)
            .FirstOrDefault();

        // ── Checklist and links between tasks ───────────────────────────────
        var subtaskId = db.Subtasks.AsNoTracking()
            .Where(s => s.Task.UserId == myId && !s.Task.IsDeleted)
            .OrderBy(s => s.CreatedAt).ThenBy(s => s.Id)
            .Select(s => (Guid?)s.Id)
            .FirstOrDefault();

        var link = db.TaskDependencies.AsNoTracking()
            .Where(d => d.Task.UserId == myId && !d.Task.IsDeleted && !d.DependsOnTask.IsDeleted)
            .OrderBy(d => d.CreatedAt).ThenBy(d => d.Id)
            .Select(d => new { d.TaskId, d.DependsOnTaskId })
            .FirstOrDefault();

        var unlinkedTaskId = db.Tasks.AsNoTracking()
            .Where(t => t.UserId == myId && !t.IsDeleted && t.Id != taskId &&
                        !db.TaskDependencies.Any(d => d.TaskId == t.Id || d.DependsOnTaskId == t.Id))
            .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefault();

        // ── Chat: one with a friend, and the newest message the account sent there ─
        var conversationId = db.ConversationParticipants.AsNoTracking()
            .Where(p => p.UserId == myId)
            .OrderByDescending(p => p.Conversation.Participants.Any(o => o.UserId != myId && friendIds.Contains(o.UserId)))
            .ThenByDescending(p => p.Conversation.LastMessageAt).ThenBy(p => p.ConversationId)
            .Select(p => (Guid?)p.ConversationId)
            .FirstOrDefault();

        var chatMessageId = db.ChatMessages.AsNoTracking()
            .Where(m => m.ConversationId == conversationId && m.SenderId == myId && !m.IsDeleted)
            .OrderByDescending(m => m.CreatedAt).ThenBy(m => m.Id)
            .Select(m => (Guid?)m.Id)
            .FirstOrDefault();

        return new SwaggerSamples
        {
            ConversationId = conversationId,
            ChatMessageId = chatMessageId,
            CalendarId = calendarId,
            EventId = plainEvent?.Id ?? repeatingEvent?.Id,
            RepeatingEventId = repeatingEvent?.Id,
            OccurrenceStart = occurrenceStart,
            AttendeeUserId = repeatingEvent?.AttendeeUserId,
            InvitedEventId = invitedEventId,
            ContactUserId = other != null && friendIds.Contains(other.Id) ? other.Id : groupMemberUserId,
            ReminderId = reminderId,
            AttachmentId = attachmentId,
            DeviceId = deviceId,
            SubtaskId = subtaskId,
            WaitingTaskId = link?.TaskId,
            DependsOnTaskId = link?.DependsOnTaskId,
            UnlinkedTaskId = unlinkedTaskId,

            IsLive = true,
            Email = me.Email,
            Password = password,
            Me = ToPerson(me),
            OtherUserId = other?.Id,
            OtherUser = other == null ? null : ToPerson(other),
            StrangerUserId = strangerId,
            BlockedUserId = blockedId,
            FriendshipId = friendshipId,
            PendingFriendshipId = pendingFriendshipId,
            GroupId = groupId,
            GroupMemberUserId = groupMemberUserId,
            NonMemberUserId = nonMemberUserId,
            TaskId = taskId,
            CommentId = commentId,
            NotificationId = notificationId,
            MentionId = mentionId,
            BoardId = boardId,
            SearchWord = SearchWordFrom(task?.Title) ?? "report"
        };
    }

    private static SamplePerson ToPerson(User user) => new(
        user.FullName,
        user.Email,
        user.PhoneNumber,
        user.Country,
        string.IsNullOrWhiteSpace(user.TimeZone) ? "UTC" : user.TimeZone,
        user.Gender?.ToString(),
        user.DateOfBirth,
        user.Bio);

    /// <summary>The longest plain word of a title: search needs at least two characters.</summary>
    private static string? SearchWordFrom(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;

        return title
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length >= 3 && word.All(char.IsLetter))
            .OrderByDescending(word => word.Length)
            .Select(word => word.ToLowerInvariant())
            .FirstOrDefault();
    }
}
