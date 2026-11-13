using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Services
{
    /// <inheritdoc />
    /// <remarks>
    /// Rules: people are added by a friend of theirs who is in the group (so
    /// never by someone they blocked or who blocked them); admins rename,
    /// remove and make admins; anyone leaves. A group is never without an
    /// admin, and one with nobody left is deleted. Missing answers 404, not
    /// being in the chat or not being an admin 403.
    /// </remarks>
    public class GroupChatService : IGroupChatService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly PeopleDirectory _people;
        private readonly IChatService _chat;
        private readonly IGroupCallService _calls;
        private readonly IRealtimeNotifier _realtime;

        public GroupChatService(
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUser,
            PeopleDirectory people,
            IChatService chat,
            IGroupCallService calls,
            IRealtimeNotifier realtime)
        {
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _people = people;
            _chat = chat;
            _calls = calls;
            _realtime = realtime;
        }

        private IGenericRepository<Conversation> Conversations => _unitOfWork.Repository<Conversation>();
        private IGenericRepository<ConversationParticipant> Participants => _unitOfWork.Repository<ConversationParticipant>();
        private IGenericRepository<ChatMessage> Messages => _unitOfWork.Repository<ChatMessage>();

        public async Task<ConversationDto> CreateAsync(CreateGroupDto dto)
        {
            var me = _currentUser.RequireUserId();
            var others = (dto.MemberIds ?? new List<Guid>()).Distinct().ToList();

            if (others.Contains(me))
                throw new ApiException("You are in the group yourself: list only the others.", 400);
            if (others.Count < Conversation.MinOthersInNewGroup)
                throw new ApiException($"A group needs at least {Conversation.MinOthersInNewGroup} other people.", 400);
            if (others.Count + 1 > Conversation.MaxGroupSize)
                throw new ApiException(TooManyPeople, 400);

            var conversation = Conversation.Group(dto.Name, me);
            await RequireFriendsAsync(me, others);

            var mine = new ConversationParticipant(conversation.Id, me, isAdmin: true);
            await Conversations.AddAsync(conversation);
            await Participants.AddAsync(mine);
            foreach (var userId in others)
                await Participants.AddAsync(new ConversationParticipant(conversation.Id, userId));

            var names = await NamesAsync(new[] { me });
            var line = await AddLineAsync(conversation, mine, ChatGroupEvent.Created,
                $"{Name(names, me)} created the group \"{conversation.Name}\"", groupName: conversation.Name);
            await _unitOfWork.CompleteAsync();

            await AnnounceAsync(conversation.Id, line);
            return await LoadAsync(conversation.Id);
        }

        public async Task<ConversationDto> RenameAsync(Guid conversationId, RenameGroupDto dto)
        {
            var group = await RequireGroupAsync(conversationId, adminOnly: true);

            if (group.Conversation.Rename(dto.Name))
            {
                var names = await NamesAsync(new[] { group.Me });
                var line = await AddLineAsync(group.Conversation, group.Mine, ChatGroupEvent.Renamed,
                    $"{Name(names, group.Me)} renamed the group to \"{group.Conversation.Name}\"", groupName: group.Conversation.Name);
                await _unitOfWork.CompleteAsync();

                await AnnounceAsync(conversationId, line);
            }

            return await LoadAsync(conversationId);
        }

        public async Task<ConversationDto> AddMembersAsync(Guid conversationId, AddMembersDto dto)
        {
            var group = await RequireGroupAsync(conversationId);

            var asked = (dto.UserIds ?? new List<Guid>()).Distinct().ToList();
            if (asked.Count == 0)
                throw new ApiException("Choose at least one person to add.", 400);

            var newIds = asked.Where(id => group.People.All(p => p.UserId != id)).ToList();
            if (newIds.Count == 0) return await LoadAsync(conversationId);

            if (group.People.Count + newIds.Count > Conversation.MaxGroupSize)
                throw new ApiException(TooManyPeople, 400);
            await RequireFriendsAsync(group.Me, newIds);

            var names = await NamesAsync(newIds.Append(group.Me));
            var lines = new List<ChatMessage>();
            foreach (var userId in newIds)
            {
                lines.Add(await AddLineAsync(group.Conversation, group.Mine, ChatGroupEvent.MemberAdded,
                    $"{Name(names, group.Me)} added {Name(names, userId)}", targetUserId: userId));
            }

            // A new member has "read" what was there before: for them the chat
            // starts unread at the first of these lines.
            var readUpTo = lines[0].CreatedAt.AddMilliseconds(-1);
            foreach (var userId in newIds)
            {
                var person = new ConversationParticipant(conversationId, userId);
                person.ReadUpTo(readUpTo);
                await Participants.AddAsync(person);
            }
            await _unitOfWork.CompleteAsync();

            await AnnounceAsync(conversationId, lines.ToArray());
            return await LoadAsync(conversationId);
        }

        public async Task<ConversationDto> RemoveMemberAsync(Guid conversationId, Guid userId)
        {
            var group = await RequireGroupAsync(conversationId, adminOnly: true);

            if (userId == group.Me)
                throw new ApiException("To go yourself, leave the group.", 400);
            var person = group.People.FirstOrDefault(p => p.UserId == userId)
                ?? throw new ApiException("That person is not in this group.", 404);

            Participants.Delete(person);

            var names = await NamesAsync(new[] { group.Me, userId });
            var line = await AddLineAsync(group.Conversation, group.Mine, ChatGroupEvent.MemberRemoved,
                $"{Name(names, group.Me)} removed {Name(names, userId)}", targetUserId: userId);
            await _unitOfWork.CompleteAsync();

            await AnnounceAsync(conversationId, line);
            await GoneForAsync(conversationId, userId);
            return await LoadAsync(conversationId);
        }

        public async Task<ConversationDto> MakeAdminAsync(Guid conversationId, Guid userId)
        {
            var group = await RequireGroupAsync(conversationId, adminOnly: true);

            var person = group.People.FirstOrDefault(p => p.UserId == userId)
                ?? throw new ApiException("That person is not in this group.", 404);

            if (person.MakeAdmin())
            {
                await _unitOfWork.CompleteAsync();
                await _chat.AnnounceGroupAsync(conversationId);
            }

            return await LoadAsync(conversationId);
        }

        public async Task LeaveAsync(Guid conversationId)
        {
            var group = await RequireGroupAsync(conversationId);
            var remaining = group.People.Where(p => p.UserId != group.Me).ToList();

            Participants.Delete(group.Mine);

            if (remaining.Count == 0)
            {
                // Nobody left: the group goes, and its messages with it.
                Conversations.Delete(group.Conversation);
                await _unitOfWork.CompleteAsync();
            }
            else
            {
                Conversation.NextAdmin(remaining)?.MakeAdmin();

                var names = await NamesAsync(new[] { group.Me });
                var line = await AddLineAsync(group.Conversation, reader: null, ChatGroupEvent.MemberLeft,
                    $"{Name(names, group.Me)} left the group", actorId: group.Me);
                await _unitOfWork.CompleteAsync();

                await AnnounceAsync(conversationId, line);
            }

            await GoneForAsync(conversationId, group.Me);
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static string TooManyPeople => $"A group can have at most {Conversation.MaxGroupSize} people.";

        /// <summary>The group, everyone in it, and the caller's own place. 404 when missing, 403 when not in it (or not an admin).</summary>
        private async Task<GroupAccess> RequireGroupAsync(Guid conversationId, bool adminOnly = false)
        {
            var me = _currentUser.RequireUserId();

            var conversation = await Conversations.GetByIdAsync(conversationId)
                ?? throw new ApiException("Chat not found.", 404);
            var people = (await Participants.FindAsync(p => p.ConversationId == conversationId)).ToList();
            var mine = people.FirstOrDefault(p => p.UserId == me)
                ?? throw new ApiException("You are not in this chat.", 403);

            if (!conversation.IsGroup)
                throw new ApiException("This is not a group chat.", 400);
            if (adminOnly && !mine.IsAdmin)
                throw new ApiException("Only an admin of the group can do that.", 403);

            return new GroupAccess(conversation, people, mine);
        }

        /// <summary>
        /// People join a group through a friend. A block ends a friendship, so
        /// this also keeps anyone from being added by someone they blocked or
        /// who blocked them.
        /// </summary>
        private async Task RequireFriendsAsync(Guid me, IEnumerable<Guid> userIds)
        {
            var friends = await _people.GetFriendIdsAsync(me);
            if (userIds.Any(id => !friends.Contains(id)))
                throw new ApiException("You can only add your friends to a group.", 403);
        }

        /// <summary>
        /// Adds a line about the group to its chat, without saving. Whoever did
        /// it (<paramref name="reader"/>) has read it.
        /// </summary>
        private async Task<ChatMessage> AddLineAsync(
            Conversation conversation, ConversationParticipant? reader, ChatGroupEvent what, string sentence,
            Guid? targetUserId = null, string? groupName = null, Guid? actorId = null)
        {
            var line = ChatMessage.ForGroupEvent(
                conversation.Id, actorId ?? reader!.UserId, what, sentence, targetUserId, groupName);
            await Messages.AddAsync(line);

            conversation.MessageSent(line.CreatedAt);
            reader?.ReadUpTo(line.CreatedAt);
            return line;
        }

        /// <summary>Shows saved lines to everyone in the group, then what the group looks like now.</summary>
        private async Task AnnounceAsync(Guid conversationId, params ChatMessage[] lines)
        {
            foreach (var line in lines) await _chat.AnnounceAsync(line.Id);
            await _chat.AnnounceGroupAsync(conversationId);
        }

        /// <summary>Someone is no longer in the group: their apps drop the chat, and they leave its call.</summary>
        private async Task GoneForAsync(Guid conversationId, Guid userId)
        {
            await _realtime.NotifyUserAsync(userId, ChatService.Events.GroupRemoved, new { conversationId });
            await _calls.LeaveForMemberAsync(conversationId, userId);
        }

        private async Task<ConversationDto> LoadAsync(Guid conversationId) =>
            await _chat.GetConversationAsync(conversationId)
            ?? throw new ApiException("Chat not found.", 404);

        private async Task<Dictionary<Guid, string>> NamesAsync(IEnumerable<Guid> userIds)
        {
            var ids = userIds.Distinct().ToList();
            var people = await _unitOfWork.Repository<User>().SelectAsync(
                u => ids.Contains(u.Id), u => new { u.Id, u.FullName });

            return people.ToDictionary(p => p.Id, p => p.FullName);
        }

        private static string Name(Dictionary<Guid, string> names, Guid userId) =>
            names.GetValueOrDefault(userId, "Someone");

        private sealed record GroupAccess(Conversation Conversation, List<ConversationParticipant> People, ConversationParticipant Mine)
        {
            public Guid Me => Mine.UserId;
        }
    }
}
