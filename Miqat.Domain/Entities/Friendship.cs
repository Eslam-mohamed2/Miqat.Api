using Miqat.Domain.Common;
using Miqat.Domain.Enumerations;

namespace Miqat.Domain.Entities
{
    public class Friendship : BaseEntity
    {
        public Guid SenderId { get; set; }
        public virtual User Sender { get; set; } = null!;

        public Guid ReceiverId { get; set; }
        public virtual User Receiver { get; set; } = null!;

        public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;

        public Friendship(Guid senderId, Guid receiverId)
        {
            if (senderId == Guid.Empty)
                throw new ArgumentException("Sender ID cannot be empty.", nameof(senderId));
            if (receiverId == Guid.Empty)
                throw new ArgumentException("Receiver ID cannot be empty.", nameof(receiverId));
            if (senderId == receiverId)
                throw new ArgumentException("A user cannot send a friend request to themselves.", nameof(receiverId));

            SenderId = senderId;
            ReceiverId = receiverId;
            Status = FriendshipStatus.Pending;
        }

        private Friendship() { }

        public void Accept()
        {
            Status = FriendshipStatus.Accepted;
            SetUpdated();
        }

        public void Reject()
        {
            Status = FriendshipStatus.Rejected;
            SetUpdated();
        }

        public void Block()
        {
            Status = FriendshipStatus.Blocked;
            SetUpdated();
        }

        /// <summary>
        /// Re-uses this row for a brand new request. There is a unique index on
        /// (SenderId, ReceiverId), so a pair that was unfriended / rejected /
        /// unblocked must revive its old row instead of inserting a second one.
        /// </summary>
        public void Reopen(Guid senderId, Guid receiverId)
        {
            if (senderId == receiverId)
                throw new ArgumentException("A user cannot send a friend request to themselves.", nameof(receiverId));

            SenderId = senderId;
            ReceiverId = receiverId;
            Status = FriendshipStatus.Pending;
            Restore();
        }

        /// <summary>
        /// Blocks <paramref name="blockedId"/>. A blocked row always stores the
        /// blocker as Sender, which is how we know who is allowed to unblock.
        /// </summary>
        public void BlockBy(Guid blockerId, Guid blockedId)
        {
            if (blockerId == blockedId)
                throw new ArgumentException("A user cannot block themselves.", nameof(blockedId));

            SenderId = blockerId;
            ReceiverId = blockedId;
            Status = FriendshipStatus.Blocked;
            Restore();
        }
    }
}
