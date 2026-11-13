using Miqat.Application.Interfaces;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Services
{
    /// <summary>
    /// Who a user is connected to: their friends, their projects and the people
    /// on those projects.
    /// <para>
    /// Search, the profile page, contacts, invitations and live updates all ask
    /// these same questions. They are answered here once, and with id-only
    /// queries: nothing is loaded that the caller is not going to use.
    /// </para>
    /// </summary>
    public class PeopleDirectory
    {
        private readonly IUnitOfWork _unitOfWork;

        public PeopleDirectory(IUnitOfWork unitOfWork) => _unitOfWork = unitOfWork;

        /// <summary>
        /// Ids of the accepted friends of the user. A deleted account is not a
        /// friend any more, also when its friendship row is still there.
        /// </summary>
        public async Task<HashSet<Guid>> GetFriendIdsAsync(Guid userId)
        {
            var friendIds = await _unitOfWork.Repository<Friendship>().SelectAsync(
                f => f.Status == FriendshipStatus.Accepted && !f.IsDeleted &&
                     (f.SenderId == userId || f.ReceiverId == userId) &&
                     !f.Sender.IsDeleted && !f.Receiver.IsDeleted,
                f => f.SenderId == userId ? f.ReceiverId : f.SenderId);

            return friendIds.ToHashSet();
        }

        /// <summary>Ids of everyone the user has blocked or is blocked by.</summary>
        public async Task<HashSet<Guid>> GetBlockedIdsAsync(Guid userId)
        {
            var blockedIds = await _unitOfWork.Repository<Friendship>().SelectAsync(
                f => f.Status == FriendshipStatus.Blocked && !f.IsDeleted &&
                     (f.SenderId == userId || f.ReceiverId == userId),
                f => f.SenderId == userId ? f.ReceiverId : f.SenderId);

            return blockedIds.ToHashSet();
        }

        /// <summary>Ids of everyone who shares a group chat with the user, without the user.</summary>
        public async Task<HashSet<Guid>> GetGroupChatMateIdsAsync(Guid userId)
        {
            var mateIds = await _unitOfWork.Repository<ConversationParticipant>().SelectAsync(
                p => p.UserId != userId && p.Conversation.IsGroup &&
                     p.Conversation.Participants.Any(mine => mine.UserId == userId),
                p => p.UserId);

            return mateIds.ToHashSet();
        }

        /// <summary>Ids of the live projects the user owns or is a member of.</summary>
        public async Task<HashSet<Guid>> GetProjectIdsAsync(Guid userId) =>
            (await GetProjectsAsync(userId)).Select(p => p.Id).ToHashSet();

        /// <summary>Everyone who sees a project: its members and its owner.</summary>
        public async Task<HashSet<Guid>> GetProjectAudienceAsync(Guid groupId)
        {
            var audience = (await _unitOfWork.Repository<GroupMember>()
                .SelectAsync(gm => gm.GroupId == groupId, gm => gm.UserId)).ToHashSet();

            audience.UnionWith(await _unitOfWork.Repository<Group>()
                .SelectAsync(g => g.Id == groupId, g => g.OwnerId));

            return audience;
        }

        /// <summary>Everyone on the projects of the user (members and owners), without the user.</summary>
        public async Task<HashSet<Guid>> GetTeammateIdsAsync(Guid userId)
        {
            var projects = await GetProjectsAsync(userId);
            var teammates = projects.Select(p => p.OwnerId).ToHashSet();

            if (projects.Count > 0)
            {
                var projectIds = projects.Select(p => p.Id).ToList();
                teammates.UnionWith(await _unitOfWork.Repository<GroupMember>()
                    .SelectAsync(gm => projectIds.Contains(gm.GroupId), gm => gm.UserId));
            }

            teammates.Remove(userId);
            return teammates;
        }

        /// <summary>
        /// Friends and teammates together: the people the user may assign work
        /// to, invite to an event, or ask for free time.
        /// </summary>
        public async Task<HashSet<Guid>> GetContactIdsAsync(Guid userId)
        {
            var contacts = await GetFriendIdsAsync(userId);
            contacts.UnionWith(await GetTeammateIdsAsync(userId));
            return contacts;
        }

        private Task<List<ProjectRef>> GetProjectsAsync(Guid userId) =>
            _unitOfWork.Repository<Group>().SelectAsync(
                g => !g.IsDeleted && (g.OwnerId == userId || g.Members.Any(m => m.UserId == userId)),
                g => new ProjectRef(g.Id, g.OwnerId));

        private sealed record ProjectRef(Guid Id, Guid OwnerId);
    }
}
