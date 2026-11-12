using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;
using TaskStatus = Miqat.Domain.Enumerations.TaskStatus;

namespace Miqat.Application.Services
{
    /// <inheritdoc />
    public class UserOverviewService : IUserOverviewService
    {
        private const int MaxMutualFriendsListed = 12;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IUserService _users;
        private readonly IFriendService _friends;
        private readonly PeopleDirectory _people;

        public UserOverviewService(
            IUnitOfWork unitOfWork, IUserService users, IFriendService friends, PeopleDirectory people)
        {
            _unitOfWork = unitOfWork;
            _users = users;
            _friends = friends;
            _people = people;
        }

        // ── Summary ───────────────────────────────────────────────────────────

        public async Task<UserSummaryDto> GetSummaryAsync(Guid userId)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId);
            var zone = TimeZones.Resolve(user?.TimeZone);

            // Only the five values the counts are made from, not whole tasks:
            // this runs every time the dashboard opens.
            var tasks = await _unitOfWork.Repository<TaskItem>().SelectAsync(
                t => (t.UserId == userId || t.AssignedToUserId == userId) && !t.IsDeleted,
                t => new TaskFacts(t.Status, t.DueDate, t.CompletedAt, t.UserId, t.AssignedToUserId));

            // "Today" is the user's own calendar day, not the server's.
            var now = DateTime.UtcNow;
            var todayStart = TimeZones.StartOfDay(now, zone);
            var todayEnd = todayStart.AddDays(1);
            var weekEnd = todayStart.AddDays(7);
            var weekAgo = now.AddDays(-7);

            var summary = new UserSummaryDto
            {
                TimeZone = zone.Id,
                Tasks = new TaskCountsDto
                {
                    Total = tasks.Count,
                    Pending = tasks.Count(t => t.Status == TaskStatus.Pending),
                    InProgress = tasks.Count(t => t.Status == TaskStatus.In_progress),
                    OnHold = tasks.Count(t => t.Status == TaskStatus.On_hold),
                    Completed = tasks.Count(t => t.Status == TaskStatus.Completed),
                    Cancelled = tasks.Count(t => t.Status == TaskStatus.Cancelled),
                    Overdue = tasks.Count(t => t.IsOpen && t.DueDate < now),
                    DueToday = tasks.Count(t => t.IsOpen && t.DueDate >= todayStart && t.DueDate < todayEnd),
                    DueThisWeek = tasks.Count(t => t.IsOpen && t.DueDate >= todayStart && t.DueDate < weekEnd),
                    CompletedThisWeek = tasks.Count(t => t.CompletedAt >= weekAgo),
                    AssignedToMe = tasks.Count(t => t.AssignedToUserId == userId && t.UserId != userId)
                }
            };

            summary.Projects = (await _people.GetProjectIdsAsync(userId)).Count;

            var friendships = await _unitOfWork.Repository<Friendship>().SelectAsync(
                f => (f.SenderId == userId || f.ReceiverId == userId) && !f.IsDeleted &&
                     (f.Status == FriendshipStatus.Accepted || f.Status == FriendshipStatus.Pending),
                f => new { f.Status, f.ReceiverId });
            summary.Friends = friendships.Count(f => f.Status == FriendshipStatus.Accepted);
            summary.PendingFriendRequests = friendships.Count(
                f => f.Status == FriendshipStatus.Pending && f.ReceiverId == userId);

            summary.UnreadNotifications = await _unitOfWork.Repository<Notification>()
                .CountAsync(n => n.RecipientUserId == userId && !n.IsRead && !n.IsDeleted);
            summary.UnreadMentions = await _unitOfWork.Repository<Mention>()
                .CountAsync(m => m.MentionedUserId == userId && !m.IsRead && !m.IsDeleted);
            summary.Boards = await _unitOfWork.Repository<Board>()
                .CountAsync(b => b.OwnerId == userId && !b.IsDeleted);

            return summary;
        }

        /// <summary>What the summary needs to know about one task.</summary>
        private sealed record TaskFacts(
            TaskStatus Status, DateTime? DueDate, DateTime? CompletedAt, Guid UserId, Guid? AssignedToUserId)
        {
            public bool IsOpen => Status != TaskStatus.Completed && Status != TaskStatus.Cancelled;
        }

        // ── Profile ───────────────────────────────────────────────────────────

        public async Task<UserProfileDto?> GetProfileAsync(Guid viewerId, Guid userId, bool viewerIsAdmin)
        {
            var isMe = viewerId == userId;

            var user = await _users.GetUserById(userId, includePrivateFields: isMe || viewerIsAdmin);
            if (user == null) return null;

            var profile = new UserProfileDto { User = user, IsMe = isMe };

            var theirFriendIds = await _people.GetFriendIdsAsync(userId);
            profile.FriendsCount = theirFriendIds.Count;

            if (isMe) return profile;

            profile.Friendship = await _friends.GetStatusAsync(viewerId, userId);

            // Friends in common.
            var myFriendIds = await _people.GetFriendIdsAsync(viewerId);
            var mutualIds = myFriendIds.Intersect(theirFriendIds).ToList();
            profile.MutualFriendsCount = mutualIds.Count;

            if (mutualIds.Count > 0)
            {
                var mutual = await _unitOfWork.Repository<User>()
                    .FindAsync(u => mutualIds.Contains(u.Id) && !u.IsDeleted);
                profile.MutualFriends = mutual
                    .OrderBy(u => u.FullName)
                    .Take(MaxMutualFriendsListed)
                    .Select(ToCard)
                    .ToList();
            }

            // Projects both people are on. Only ever projects the viewer is on
            // too, so nothing about the other person's private work leaks.
            var sharedIds = (await _people.GetProjectIdsAsync(viewerId))
                .Intersect(await _people.GetProjectIdsAsync(userId))
                .ToList();

            if (sharedIds.Count > 0)
            {
                var projects = await _unitOfWork.Repository<Group>()
                    .FindAsync(g => sharedIds.Contains(g.Id) && !g.IsDeleted);

                var taskCounts = await _unitOfWork.Repository<TaskItem>()
                    .CountGroupedAsync(
                        t => t.GroupId.HasValue && sharedIds.Contains(t.GroupId.Value) && !t.IsDeleted &&
                             (t.UserId == userId || t.AssignedToUserId == userId),
                        t => t.GroupId!.Value);

                profile.SharedProjects = projects
                    .OrderBy(g => g.Name)
                    .Select(g => new SharedProjectDto
                    {
                        Id = g.Id,
                        Name = g.Name,
                        Color = g.Color,
                        OwnerId = g.OwnerId,
                        TaskCount = taskCounts.TryGetValue(g.Id, out var count) ? count : 0
                    })
                    .ToList();
            }

            return profile;
        }

        // ── Contacts ──────────────────────────────────────────────────────────

        public async Task<IEnumerable<ContactDto>> GetContactsAsync(Guid userId)
        {
            var friendIds = await _people.GetFriendIdsAsync(userId);
            var teammateIds = await _people.GetTeammateIdsAsync(userId);

            var allIds = friendIds.Union(teammateIds).ToList();
            if (allIds.Count == 0) return new List<ContactDto>();

            var users = await _unitOfWork.Repository<User>()
                .FindAsync(u => allIds.Contains(u.Id) && !u.IsDeleted && u.IsActive);

            return users
                .OrderBy(u => u.FullName)
                .Select(u =>
                {
                    var isFriend = friendIds.Contains(u.Id);
                    var isTeammate = teammateIds.Contains(u.Id);
                    return new ContactDto
                    {
                        Id = u.Id,
                        FullName = u.FullName,
                        Email = u.Email,
                        ProfilePictureUrl = u.ProfilePictureUrl,
                        IsFriend = isFriend,
                        IsTeammate = isTeammate,
                        Relationship = isFriend && isTeammate ? "Friend and teammate"
                            : isFriend ? "Friend"
                            : "Teammate"
                    };
                })
                .ToList();
        }

        private static UserCardDto ToCard(User user) => new()
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            ProfilePictureUrl = user.ProfilePictureUrl,
            Country = user.Country
        };
    }
}
