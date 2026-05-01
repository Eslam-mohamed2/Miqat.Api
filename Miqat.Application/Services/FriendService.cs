using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Services
{
    public class FriendService : IFriendService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly INotificationService _notificationService;

        public FriendService(IUnitOfWork unitOfWork, INotificationService notificationService)
        {
            _unitOfWork = unitOfWork;
            _notificationService = notificationService;
        }

        public async Task<FriendshipDto> SendFriendRequestAsync(Guid senderId, Guid receiverId)
        {
            if (senderId == receiverId)
                throw new InvalidOperationException("A user cannot send a friend request to themselves.");

            // Check if sender and receiver exist
            var sender = await _unitOfWork.Repository<User>().GetByIdAsync(senderId);
            if (sender == null)
                throw new InvalidOperationException($"Sender with ID {senderId} not found.");

            var receiver = await _unitOfWork.Repository<User>().GetByIdAsync(receiverId);
            if (receiver == null)
                throw new InvalidOperationException($"Receiver with ID {receiverId} not found.");

            // Check if friendship already exists
            var existingFriendship = await _unitOfWork.Repository<Friendship>()
                .FindAsync(f => (f.SenderId == senderId && f.ReceiverId == receiverId) ||
                                (f.SenderId == receiverId && f.ReceiverId == senderId));

            if (existingFriendship.Any())
                throw new InvalidOperationException("A friendship request already exists between these users.");

            var friendship = new Friendship(senderId, receiverId);
            await _unitOfWork.Repository<Friendship>().AddAsync(friendship);
            await _unitOfWork.CompleteAsync();

            // Create notification for receiver
            var notification = new Notification(
                title: "New Friend Request",
                message: $"{sender.FullName} sent you a friend request.",
                type: NotificationType.TaskAssigned,
                recipientUserId: receiverId,
                triggeredByUserId: senderId,
                linkedEntityId: friendship.Id,
                linkedEntityType: "Friendship"
            );
            await _unitOfWork.Repository<Notification>().AddAsync(notification);
            await _unitOfWork.CompleteAsync();

            return MapToDto(friendship, sender, receiver);
        }

        public async Task<bool> AcceptFriendRequestAsync(Guid friendshipId, Guid receiverId)
        {
            var friendship = await _unitOfWork.Repository<Friendship>().GetByIdAsync(friendshipId);
            if (friendship == null)
                return false;

            if (friendship.ReceiverId != receiverId)
                throw new InvalidOperationException("Only the receiver can accept this friend request.");

            friendship.Accept();
            _unitOfWork.Repository<Friendship>().Update(friendship);
            await _unitOfWork.CompleteAsync();

            // Create notification for sender
            var sender = await _unitOfWork.Repository<User>().GetByIdAsync(friendship.SenderId);
            var receiver = await _unitOfWork.Repository<User>().GetByIdAsync(friendship.ReceiverId);

            var notification = new Notification(
                title: "Friend Request Accepted",
                message: $"{receiver?.FullName} accepted your friend request.",
                type: NotificationType.TaskCompleted,
                recipientUserId: friendship.SenderId,
                triggeredByUserId: receiverId,
                linkedEntityId: friendship.Id,
                linkedEntityType: "Friendship"
            );
            await _unitOfWork.Repository<Notification>().AddAsync(notification);
            await _unitOfWork.CompleteAsync();

            return true;
        }

        public async Task<bool> RejectFriendRequestAsync(Guid friendshipId, Guid receiverId)
        {
            var friendship = await _unitOfWork.Repository<Friendship>().GetByIdAsync(friendshipId);
            if (friendship == null)
                return false;

            if (friendship.ReceiverId != receiverId)
                throw new InvalidOperationException("Only the receiver can reject this friend request.");

            friendship.Reject();
            _unitOfWork.Repository<Friendship>().Update(friendship);
            await _unitOfWork.CompleteAsync();

            return true;
        }

        public async Task<bool> BlockUserAsync(Guid userId, Guid userToBlockId)
        {
            if (userId == userToBlockId)
                throw new InvalidOperationException("A user cannot block themselves.");

            // Check if users exist
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId);
            if (user == null)
                throw new InvalidOperationException($"User with ID {userId} not found.");

            var userToBlock = await _unitOfWork.Repository<User>().GetByIdAsync(userToBlockId);
            if (userToBlock == null)
                throw new InvalidOperationException($"User with ID {userToBlockId} not found.");

            // Check if a friendship exists
            var existingFriendship = await _unitOfWork.Repository<Friendship>()
                .FindAsync(f => (f.SenderId == userId && f.ReceiverId == userToBlockId) ||
                                (f.SenderId == userToBlockId && f.ReceiverId == userId));

            if (existingFriendship.Any())
            {
                var friendship = existingFriendship.First();
                friendship.Block();
                _unitOfWork.Repository<Friendship>().Update(friendship);
            }
            else
            {
                // Create new blocked friendship
                var friendship = new Friendship(userId, userToBlockId);
                friendship.Block();
                await _unitOfWork.Repository<Friendship>().AddAsync(friendship);
            }

            await _unitOfWork.CompleteAsync();
            return true;
        }

        public async Task<bool> UnblockUserAsync(Guid userId, Guid blockedUserId)
        {
            var friendship = await _unitOfWork.Repository<Friendship>()
                .FindAsync(f => (f.SenderId == userId && f.ReceiverId == blockedUserId && f.Status == FriendshipStatus.Blocked) ||
                                (f.SenderId == blockedUserId && f.ReceiverId == userId && f.Status == FriendshipStatus.Blocked));

            if (!friendship.Any())
                return false;

            var friendshipToUnblock = friendship.First();
            friendshipToUnblock.SoftDelete();
            _unitOfWork.Repository<Friendship>().Update(friendshipToUnblock);
            await _unitOfWork.CompleteAsync();

            return true;
        }

        public async Task<IEnumerable<FriendshipDto>> GetFriendsAsync(Guid userId)
        {
            var friendships = await _unitOfWork.Repository<Friendship>()
                .FindAsync(f => f.Status == FriendshipStatus.Accepted &&
                                (f.SenderId == userId || f.ReceiverId == userId) &&
                                !f.IsDeleted);

            var dtos = new List<FriendshipDto>();
            foreach (var friendship in friendships)
            {
                var otherUserId = friendship.SenderId == userId ? friendship.ReceiverId : friendship.SenderId;
                var otherUser = await _unitOfWork.Repository<User>().GetByIdAsync(otherUserId);
                var sender = await _unitOfWork.Repository<User>().GetByIdAsync(friendship.SenderId);
                var receiver = await _unitOfWork.Repository<User>().GetByIdAsync(friendship.ReceiverId);

                dtos.Add(MapToDto(friendship, sender, receiver));
            }

            return dtos;
        }

        public async Task<IEnumerable<FriendshipDto>> GetPendingRequestsAsync(Guid userId)
        {
            var friendships = await _unitOfWork.Repository<Friendship>()
                .FindAsync(f => f.Status == FriendshipStatus.Pending &&
                                f.ReceiverId == userId &&
                                !f.IsDeleted);

            var dtos = new List<FriendshipDto>();
            foreach (var friendship in friendships)
            {
                var sender = await _unitOfWork.Repository<User>().GetByIdAsync(friendship.SenderId);
                var receiver = await _unitOfWork.Repository<User>().GetByIdAsync(friendship.ReceiverId);
                dtos.Add(MapToDto(friendship, sender, receiver));
            }

            return dtos;
        }

        public async Task<IEnumerable<FriendshipDto>> GetSentRequestsAsync(Guid userId)
        {
            var friendships = await _unitOfWork.Repository<Friendship>()
                .FindAsync(f => f.Status == FriendshipStatus.Pending &&
                                f.SenderId == userId &&
                                !f.IsDeleted);

            var dtos = new List<FriendshipDto>();
            foreach (var friendship in friendships)
            {
                var sender = await _unitOfWork.Repository<User>().GetByIdAsync(friendship.SenderId);
                var receiver = await _unitOfWork.Repository<User>().GetByIdAsync(friendship.ReceiverId);
                dtos.Add(MapToDto(friendship, sender, receiver));
            }

            return dtos;
        }

        public async Task<FriendshipDto?> GetFriendshipAsync(Guid friendshipId)
        {
            var friendship = await _unitOfWork.Repository<Friendship>().GetByIdAsync(friendshipId);
            if (friendship == null || friendship.IsDeleted)
                return null;

            var sender = await _unitOfWork.Repository<User>().GetByIdAsync(friendship.SenderId);
            var receiver = await _unitOfWork.Repository<User>().GetByIdAsync(friendship.ReceiverId);

            return MapToDto(friendship, sender, receiver);
        }

        private FriendshipDto MapToDto(Friendship friendship, User? sender, User? receiver)
        {
            return new FriendshipDto
            {
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
