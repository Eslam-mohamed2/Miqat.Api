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
    }
}
