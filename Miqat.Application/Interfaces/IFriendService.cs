using Miqat.Application.Modules;

namespace Miqat.Application.Interfaces
{
    public interface IFriendService
    {
        Task<FriendshipDto> SendFriendRequestAsync(Guid senderId, Guid receiverId);
        Task<bool> AcceptFriendRequestAsync(Guid friendshipId, Guid receiverId);
        Task<bool> RejectFriendRequestAsync(Guid friendshipId, Guid receiverId);
        Task<bool> BlockUserAsync(Guid userId, Guid userToBlockId);
        Task<IEnumerable<FriendshipDto>> GetFriendsAsync(Guid userId);
        Task<IEnumerable<FriendshipDto>> GetPendingRequestsAsync(Guid userId);
        Task<IEnumerable<FriendshipDto>> GetSentRequestsAsync(Guid userId);
        Task<bool> UnblockUserAsync(Guid userId, Guid blockedUserId);
        Task<FriendshipDto?> GetFriendshipAsync(Guid friendshipId);
    }
}
