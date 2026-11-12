using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Specifications.Users;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Services
{
    /// <summary>
    /// There is at most one friendship row per pair of users (the service checks
    /// both directions before inserting, and the table has a unique index on
    /// SenderId + ReceiverId). A row that was unfriended, rejected or unblocked
    /// is therefore re-used ("reopened") by the next request instead of
    /// inserting a second row.
    ///
    /// A Blocked row always has the blocker as Sender, so only the blocker can
    /// unblock.
    /// </summary>
    public class FriendService : IFriendService
    {
        private const int MaxSuggestions = 20;

        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationSender _notifications;

        public FriendService(IUnitOfWork unitOfWork, INotificationSender notifications)
        {
            _unitOfWork = unitOfWork;
            _notifications = notifications;
        }

        public async Task<FriendshipDto> SendFriendRequestAsync(Guid senderId, Guid receiverId)
        {
            if (senderId == receiverId)
                throw new ApiException("You cannot send a friend request to yourself.", 400);

            var sender = await FindActiveUserAsync(senderId)
                ?? throw new ApiException("User not found.", 404);
            var receiver = await FindActiveUserAsync(receiverId)
                ?? throw new ApiException("User not found.", 404);

            var friendship = await FindBetweenAsync(senderId, receiverId);

            if (friendship == null)
            {
                friendship = new Friendship(senderId, receiverId);
                await _unitOfWork.Repository<Friendship>().AddAsync(friendship);
            }
            else if (friendship.Status == FriendshipStatus.Blocked && !friendship.IsDeleted)
            {
                // Same message whoever did the blocking, so a block is not revealed.
                throw new ApiException("You cannot send a friend request to this user.", 400);
            }
            else if (!friendship.IsDeleted && friendship.Status == FriendshipStatus.Accepted)
            {
                throw new ApiException("You are already friends with this user.", 400);
            }
            else if (!friendship.IsDeleted && friendship.Status == FriendshipStatus.Pending)
            {
                throw new ApiException(
                    friendship.SenderId == senderId
                        ? "You have already sent a friend request to this user."
                        : "This user has already sent you a friend request.", 400);
            }
            else
            {
                // Unfriended, rejected or unblocked earlier: start over on the same row.
                friendship.Reopen(senderId, receiverId);
            }

            var staged = await _notifications.StageAsync(
                new NotificationContent(
                    NotificationType.FriendRequestSent, "New Friend Request",
                    actor => $"{actor} sent you a friend request.",
                    senderId, friendship.Id, "Friendship"),
                new[] { receiverId });

            // Saved together with the request itself.
            await _unitOfWork.CompleteAsync();
            await _notifications.DeliverAsync(staged);

            return MapToDto(friendship, sender, receiver, senderId);
        }

        public async Task<bool> AcceptFriendRequestAsync(Guid friendshipId, Guid receiverId)
        {
            var friendship = await FindForParticipantAsync(friendshipId, receiverId);
            if (friendship == null)
                return false;

            if (friendship.ReceiverId != receiverId)
                throw new ApiException("Only the receiver can accept this friend request.", 403);

            if (friendship.Status != FriendshipStatus.Pending)
                throw new ApiException("This friend request is no longer pending.", 400);

            friendship.Accept();

            var staged = await _notifications.StageAsync(
                new NotificationContent(
                    NotificationType.FriendRequestAccepted, "Friend Request Accepted",
                    actor => $"{actor} accepted your friend request.",
                    receiverId, friendship.Id, "Friendship"),
                new[] { friendship.SenderId });

            await _unitOfWork.CompleteAsync();
            await _notifications.DeliverAsync(staged);

            return true;
        }

        public async Task<bool> RejectFriendRequestAsync(Guid friendshipId, Guid receiverId)
        {
            var friendship = await FindForParticipantAsync(friendshipId, receiverId);
            if (friendship == null)
                return false;

            if (friendship.ReceiverId != receiverId)
                throw new ApiException("Only the receiver can reject this friend request.", 403);

            if (friendship.Status != FriendshipStatus.Pending)
                throw new ApiException("This friend request is no longer pending.", 400);

            friendship.Reject();
            await _unitOfWork.CompleteAsync();
            return true;
        }

        public async Task<bool> BlockUserAsync(Guid userId, Guid userToBlockId)
        {
            if (userId == userToBlockId)
                throw new ApiException("You cannot block yourself.", 400);

            var userToBlock = await _unitOfWork.Repository<User>().GetByIdAsync(userToBlockId);
            if (userToBlock == null || userToBlock.IsDeleted)
                throw new ApiException("User not found.", 404);

            var friendship = await FindBetweenAsync(userId, userToBlockId);

            if (friendship == null)
            {
                friendship = new Friendship(userId, userToBlockId);
                friendship.Block();
                await _unitOfWork.Repository<Friendship>().AddAsync(friendship);
            }
            else if (friendship.Status == FriendshipStatus.Blocked && !friendship.IsDeleted)
            {
                if (friendship.SenderId == userId)
                    return true; // already blocked by this user

                // The other user blocked first; do not let this call take the block over.
                throw new ApiException("You cannot block this user.", 400);
            }
            else
            {
                friendship.BlockBy(userId, userToBlockId);
            }

            await _unitOfWork.CompleteAsync();
            return true;
        }

        public async Task<bool> UnblockUserAsync(Guid userId, Guid blockedUserId)
        {
            // Only the blocker (stored as Sender) may unblock.
            var matches = await _unitOfWork.Repository<Friendship>()
                .FindAsync(f => f.SenderId == userId &&
                                f.ReceiverId == blockedUserId &&
                                f.Status == FriendshipStatus.Blocked &&
                                !f.IsDeleted);

            var friendship = matches.FirstOrDefault();
            if (friendship == null)
                return false;

            friendship.SoftDelete(userId.ToString());
            await _unitOfWork.CompleteAsync();
            return true;
        }

        public async Task<IEnumerable<UserDto>> GetBlockedUsersAsync(Guid userId)
        {
            var blocks = await _unitOfWork.Repository<Friendship>()
                .FindAsync(f => f.SenderId == userId &&
                                f.Status == FriendshipStatus.Blocked &&
                                !f.IsDeleted);

            var blockedIds = blocks.Select(f => f.ReceiverId).Distinct().ToList();
            if (blockedIds.Count == 0) return new List<UserDto>();

            var users = await _unitOfWork.Repository<User>()
                .FindAsync(u => blockedIds.Contains(u.Id) && !u.IsDeleted);

            return users.OrderBy(u => u.FullName).Select(ToCard).ToList();
        }

        public async Task<IEnumerable<FriendshipDto>> GetFriendsAsync(Guid userId)
        {
            var friendships = await _unitOfWork.Repository<Friendship>()
                .FindAsync(f => f.Status == FriendshipStatus.Accepted &&
                                (f.SenderId == userId || f.ReceiverId == userId) &&
                                !f.IsDeleted && !f.Sender.IsDeleted && !f.Receiver.IsDeleted);

            return await MapToDtosAsync(friendships, userId);
        }

        public async Task<IEnumerable<FriendshipDto>> GetPendingRequestsAsync(Guid userId)
        {
            var friendships = await _unitOfWork.Repository<Friendship>()
                .FindAsync(f => f.Status == FriendshipStatus.Pending &&
                                f.ReceiverId == userId &&
                                !f.IsDeleted && !f.Sender.IsDeleted);

            return await MapToDtosAsync(friendships, userId);
        }

        public async Task<IEnumerable<FriendshipDto>> GetSentRequestsAsync(Guid userId)
        {
            var friendships = await _unitOfWork.Repository<Friendship>()
                .FindAsync(f => f.Status == FriendshipStatus.Pending &&
                                f.SenderId == userId &&
                                !f.IsDeleted && !f.Receiver.IsDeleted);

            return await MapToDtosAsync(friendships, userId);
        }

        public async Task<FriendshipDto?> GetFriendshipAsync(Guid friendshipId, Guid currentUserId)
        {
            var friendship = await FindForParticipantAsync(friendshipId, currentUserId);
            if (friendship == null)
                return null;

            // The blocked side must not be able to see the block.
            if (friendship.Status == FriendshipStatus.Blocked && friendship.SenderId != currentUserId)
                return null;

            return (await MapToDtosAsync(new[] { friendship }, currentUserId)).First();
        }

        public async Task DeleteAsync(Guid friendshipId, Guid currentUserId)
        {
            var friendship = await FindForParticipantAsync(friendshipId, currentUserId)
                ?? throw new ApiException("Friendship not found.", 404);

            // Removing a block goes through unblock, and only for the blocker.
            if (friendship.Status == FriendshipStatus.Blocked)
            {
                if (friendship.SenderId != currentUserId)
                    throw new ApiException("Friendship not found.", 404);

                throw new ApiException("Use unblock to remove a block.", 400);
            }

            friendship.SoftDelete(currentUserId.ToString());
            await _unitOfWork.CompleteAsync();
        }

        public async Task<FriendshipStatusDto> GetStatusAsync(Guid currentUserId, Guid targetUserId)
        {
            var friendship = await FindBetweenAsync(currentUserId, targetUserId);

            if (friendship == null || friendship.IsDeleted)
                return new FriendshipStatusDto();

            var status = friendship.Status switch
            {
                FriendshipStatus.Accepted => "Friends",
                FriendshipStatus.Pending when friendship.SenderId == currentUserId => "Sent",
                FriendshipStatus.Pending => "Pending",
                // Only the blocker is told about the block.
                FriendshipStatus.Blocked when friendship.SenderId == currentUserId => "Blocked",
                _ => "None"
            };

            return new FriendshipStatusDto
            {
                Status = status,
                Id = status == "None" ? null : friendship.Id
            };
        }

        public async Task<IEnumerable<UserDto>> GetSuggestionsAsync(Guid currentUserId)
        {
            // Everyone the user already has a live relationship with (friend, pending, blocked).
            var existing = await _unitOfWork.Repository<Friendship>()
                .FindAsync(f => (f.SenderId == currentUserId || f.ReceiverId == currentUserId) &&
                                !f.IsDeleted &&
                                f.Status != FriendshipStatus.Rejected);

            var excludedIds = existing
                .Select(f => f.SenderId == currentUserId ? f.ReceiverId : f.SenderId)
                .Append(currentUserId)
                .Distinct()
                .ToList();

            // First choice: people who share a project with the user.
            var myMemberships = await _unitOfWork.Repository<GroupMember>()
                .FindAsync(gm => gm.UserId == currentUserId && !gm.Group.IsDeleted);
            var myOwnedGroups = await _unitOfWork.Repository<Group>()
                .FindAsync(g => g.OwnerId == currentUserId && !g.IsDeleted);

            var groupIds = myMemberships.Select(gm => gm.GroupId)
                .Concat(myOwnedGroups.Select(g => g.Id))
                .Distinct()
                .ToList();

            var suggested = new List<User>();

            if (groupIds.Count > 0)
            {
                var coMembers = await _unitOfWork.Repository<GroupMember>()
                    .FindAsync(gm => groupIds.Contains(gm.GroupId) &&
                                     !excludedIds.Contains(gm.UserId));
                var coMemberIds = coMembers.Select(gm => gm.UserId).Distinct().ToList();

                if (coMemberIds.Count > 0)
                {
                    var users = await _unitOfWork.Repository<User>()
                        .FindAsync(u => coMemberIds.Contains(u.Id) &&
                                        !u.IsDeleted && u.IsActive && u.IsVerified);
                    suggested.AddRange(users.OrderBy(u => u.FullName).Take(MaxSuggestions));
                }
            }

            // Fallback: any other active users.
            if (suggested.Count == 0)
            {
                var spec = new SuggestedUsersSpec(excludedIds, MaxSuggestions);
                suggested.AddRange(await _unitOfWork.Repository<User>().ListAsync(spec));
            }

            return suggested.Select(ToCard).ToList();
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private async Task<User?> FindActiveUserAsync(Guid id)
        {
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(id);
            return user == null || user.IsDeleted || !user.IsActive ? null : user;
        }

        /// <summary>The single row for this pair, in either direction, deleted or not.</summary>
        private async Task<Friendship?> FindBetweenAsync(Guid a, Guid b)
        {
            var rows = await _unitOfWork.Repository<Friendship>()
                .FindAsync(f => (f.SenderId == a && f.ReceiverId == b) ||
                                (f.SenderId == b && f.ReceiverId == a));

            // Old data may hold one row per direction; prefer the live one, then
            // the one already stored as a -> b.
            return rows
                .OrderBy(f => f.IsDeleted)
                .ThenBy(f => f.SenderId == a ? 0 : 1)
                .ThenByDescending(f => f.CreatedAt)
                .FirstOrDefault();
        }

        /// <summary>A live friendship the user takes part in, otherwise null.</summary>
        private async Task<Friendship?> FindForParticipantAsync(Guid friendshipId, Guid userId)
        {
            var friendship = await _unitOfWork.Repository<Friendship>().GetByIdAsync(friendshipId);
            if (friendship == null || friendship.IsDeleted)
                return null;
            if (friendship.SenderId != userId && friendship.ReceiverId != userId)
                return null;
            return friendship;
        }

        /// <param name="viewerId">Whose list this is; decides which side is "the friend".</param>
        private async Task<List<FriendshipDto>> MapToDtosAsync(IEnumerable<Friendship> friendships, Guid viewerId)
        {
            var list = friendships.OrderByDescending(f => f.UpdatedAt ?? f.CreatedAt).ToList();
            if (list.Count == 0) return new List<FriendshipDto>();

            var userIds = list.SelectMany(f => new[] { f.SenderId, f.ReceiverId }).Distinct().ToList();
            var users = (await _unitOfWork.Repository<User>().FindAsync(u => userIds.Contains(u.Id)))
                .ToDictionary(u => u.Id);

            return list.Select(f => MapToDto(
                f,
                users.GetValueOrDefault(f.SenderId),
                users.GetValueOrDefault(f.ReceiverId),
                viewerId)).ToList();
        }

        /// <summary>The public "card" of a user: no phone number, birthday or account flags.</summary>
        private static UserDto ToCard(User user) => new()
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Country = user.Country,
            ProfilePictureUrl = user.ProfilePictureUrl
        };

        private static UserCardDto? ToCardDto(User? user) => user == null ? null : new UserCardDto
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            ProfilePictureUrl = user.ProfilePictureUrl,
            Country = user.Country
        };

        private static FriendshipDto MapToDto(Friendship friendship, User? sender, User? receiver, Guid viewerId)
        {
            var sentByMe = friendship.SenderId == viewerId;
            var friend = sentByMe ? receiver : sender;

            return new FriendshipDto
            {
                FriendId = sentByMe ? friendship.ReceiverId : friendship.SenderId,
                FriendName = friend?.FullName,
                FriendEmail = friend?.Email,
                FriendProfilePictureUrl = friend?.ProfilePictureUrl,
                SentByMe = sentByMe,
                Sender = ToCardDto(sender),
                Receiver = ToCardDto(receiver),
                FriendUser = ToCardDto(friend),
                Id = friendship.Id,
                SenderId = friendship.SenderId,
                SenderName = sender?.FullName,
                SenderProfilePictureUrl = sender?.ProfilePictureUrl,
                ReceiverId = friendship.ReceiverId,
                ReceiverName = receiver?.FullName,
                ReceiverProfilePictureUrl = receiver?.ProfilePictureUrl,
                Status = friendship.Status.ToString(),
                CreatedAt = friendship.CreatedAt,
                UpdatedAt = friendship.UpdatedAt
            };
        }
    }
}
