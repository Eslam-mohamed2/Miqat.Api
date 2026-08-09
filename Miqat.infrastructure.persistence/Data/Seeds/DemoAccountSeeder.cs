using Microsoft.EntityFrameworkCore;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;
using System;
using System.Linq;
using System.Threading.Tasks;
using TaskStatusEnum = Miqat.Domain.Enumerations.TaskStatus;

namespace Miqat.infrastructure.persistence.Data.Seeds
{
    /// <summary>
    /// Builds a living demo world around the owner's real account so every flow
    /// can be exercised from a fresh sign-in: projects he owns and belongs to,
    /// teammates inside them, tasks with future due dates, comment threads, a
    /// pending friend request to accept, and the notifications each of those
    /// would have produced.
    ///
    /// Unlike the base seeders this runs on EVERY startup and is idempotent —
    /// keyed on the account's email — because the base pipeline only fires into
    /// an empty database and this account must also appear on databases that
    /// were seeded before it existed.
    /// </summary>
    public class DemoAccountSeeder
    {
        private const string OwnerEmail = "eslammohamed34564@gmail.com";
        private const string OwnerName = "Eslam Mohamed";

        /// <summary>Shared with the account owner; meets the password policy.</summary>
        private const string InitialPassword = "Miqat@2026";

        private readonly MiqatDbContext _context;

        public DemoAccountSeeder(MiqatDbContext context)
        {
            _context = context;
        }

        public async Task SeedAsync()
        {
            // Idempotency is keyed on the demo project, not the account: the owner
            // registered this email themselves during testing, so "user exists"
            // must mean "attach the demo world to it", not "skip".
            var eslam = await _context.Users
                .FirstOrDefaultAsync(u => u.Email == OwnerEmail);

            if (eslam != null &&
                await _context.Groups.AnyAsync(g => g.OwnerId == eslam.Id && g.Name == "Miqat Launch"))
            {
                Console.WriteLine("[DemoSeed] ℹ️ Demo world already present. Skipping.");
                return;
            }

            Console.WriteLine($"[DemoSeed] 🔄 Creating demo world for {OwnerEmail}...");

            if (eslam == null)
            {
                eslam = new User(
                    OwnerName, OwnerEmail,
                    new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    null, "Egypt", null, "Africa/Cairo")
                {
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword(InitialPassword),
                    IsVerified = true
                };
                _context.Users.Add(eslam);
            }
            // An existing account keeps its own password untouched.

            // ── Teammates ────────────────────────────────────────────────────
            var teammates = new[]
            {
                ("Sara Adel", "sara.adel@miqat.demo"),
                ("Omar Khaled", "omar.khaled@miqat.demo"),
                ("Nour Hassan", "nour.hassan@miqat.demo"),
                ("Youssef Tarek", "youssef.tarek@miqat.demo")
            }.Select(pair => new User(
                pair.Item1, pair.Item2,
                new DateTime(1998, 6, 15, 0, 0, 0, DateTimeKind.Utc),
                null, "Egypt", null, "Africa/Cairo")
            {
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(InitialPassword),
                IsVerified = true
            }).ToList();
            _context.Users.AddRange(teammates);

            var sara = teammates[0];
            var omar = teammates[1];
            var nour = teammates[2];
            var youssef = teammates[3];

            // ── Projects: one he owns, one he was invited into ───────────────
            var launch = new Group("Miqat Launch",
                "Everything needed to ship Miqat v1 to real users.", eslam.Id, "#2ec4a0");
            var mobile = new Group("Mobile App",
                "The companion app — Sara runs this one.", sara.Id, "#7c8ef5");
            _context.Groups.AddRange(launch, mobile);

            _context.GroupMembers.AddRange(
                new GroupMember(launch.Id, sara.Id),
                new GroupMember(launch.Id, omar.Id),
                new GroupMember(launch.Id, nour.Id),
                new GroupMember(mobile.Id, eslam.Id),
                new GroupMember(mobile.Id, youssef.Id));

            // ── Tasks with FUTURE due dates (the base seed is entirely overdue,
            //    which made every screen shout at the user) ────────────────────
            var now = DateTime.UtcNow;

            TaskItem NewTask(string title, string desc, User owner, Group group,
                int dueInDays, TaskStatusEnum status, Priority priority, User? assignee)
            {
                var task = new TaskItem(title, desc, owner.Id, priority,
                    now.AddDays(dueInDays), assignee?.Id, group.Id,
                    "launch,demo", RecurrencePattern.None, null)
                {
                    Status = status
                };
                return task;
            }

            var designReview = NewTask("Review the landing page design",
                "Sara's mockups are in Figma — leave feedback in the thread.",
                eslam, launch, 2, TaskStatusEnum.In_progress, Priority.High, sara);
            var apiHardening = NewTask("Harden the API before launch",
                "Authorization, rate limits and tests are in; needs a final pass.",
                eslam, launch, 5, TaskStatusEnum.In_progress, Priority.Critical, omar);
            var pressKit = NewTask("Prepare the press kit",
                "Screenshots, copy, and the product one-pager.",
                nour, launch, 9, TaskStatusEnum.Pending, Priority.Medium, eslam);
            var betaList = NewTask("Invite the beta testers",
                "First 50 sign-ups from the waitlist.",
                eslam, launch, 12, TaskStatusEnum.Pending, Priority.Medium, null);
            var iosShell = NewTask("Set up the iOS app shell",
                "Capacitor wrapper around the web build.",
                sara, mobile, 7, TaskStatusEnum.In_progress, Priority.High, eslam);

            _context.Tasks.AddRange(designReview, apiHardening, pressKit, betaList, iosShell);

            // ── Comment threads ──────────────────────────────────────────────
            _context.Comments.AddRange(
                new Comment("First draft is up — be brutal, we ship in two weeks.", designReview.Id, sara.Id),
                new Comment("The hero section is strong. The pricing table needs work though.", designReview.Id, eslam.Id),
                new Comment("Agreed on pricing — I'll have a v2 tomorrow morning.", designReview.Id, sara.Id),
                new Comment("Rate limiting is live on all auth endpoints now.", apiHardening.Id, omar.Id),
                new Comment("Nice. Let's add the reuse-detection on refresh tokens next sprint.", apiHardening.Id, eslam.Id),
                new Comment("Assigned this to you Eslam — you have the best screenshots.", pressKit.Id, nour.Id));

            // ── Friendships: one accepted, one PENDING for him to accept ─────
            var acceptedFriend = new Friendship(eslam.Id, sara.Id);
            acceptedFriend.Accept();
            var pendingRequest = new Friendship(omar.Id, eslam.Id); // stays Pending
            _context.Friendships.AddRange(acceptedFriend, pendingRequest);

            // ── The notifications those events would have produced ───────────
            _context.Notifications.AddRange(
                new Notification("New Friend Request",
                    $"{omar.FullName} sent you a friend request.",
                    NotificationType.FriendRequestSent,
                    eslam.Id, omar.Id, pendingRequest.Id, "Friendship"),
                new Notification("Added to a project",
                    $"{sara.FullName} added you to \"{mobile.Name}\".",
                    NotificationType.GroupInvite,
                    eslam.Id, sara.Id, mobile.Id, "Group"),
                new Notification("New comment",
                    $"{sara.FullName} commented on \"{designReview.Title}\": First draft is up — be brutal…",
                    NotificationType.MentionedInTask,
                    eslam.Id, sara.Id, designReview.Id, "TaskItem"),
                new Notification("Task assigned",
                    $"{nour.FullName} assigned \"{pressKit.Title}\" to you.",
                    NotificationType.TaskAssigned,
                    eslam.Id, nour.Id, pressKit.Id, "TaskItem"));

            await _context.SaveChangesAsync();

            Console.WriteLine("[DemoSeed] ✅ Demo world ready: " +
                "2 projects, 5 teammates, 5 tasks, 6 comments, 1 pending friend request.");
        }
    }
}
