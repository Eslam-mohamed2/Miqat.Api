using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;

namespace Miqat.Application.Services
{
    /// <inheritdoc />
    /// <remarks>
    /// Rules: members of the group only; a person is in one call at a time,
    /// counting calls between two; the call is tied to the tab that joined it;
    /// it ends when the last person leaves. Who is in a call is kept by the
    /// <see cref="CallRegistry"/>, next to the calls between two.
    /// </remarks>
    public class GroupCallService : IGroupCallService
    {
        /// <summary>The live events of a group call, in one place for the server and the app.</summary>
        public static class Events
        {
            public const string Started = "groupCallStarted";
            public const string Incoming = "groupCallIncoming";
            public const string Joined = "groupCallJoined";
            public const string Left = "groupCallLeft";
            public const string Ended = "groupCallEnded";
            public const string Dismissed = "groupCallDismissed";
            public const string Signal = "groupCallSignal";
        }

        private readonly IUnitOfWork _unitOfWork;
        private readonly IChatService _chat;
        private readonly PeopleDirectory _people;
        private readonly IPresenceTracker _presence;
        private readonly CallRegistry _registry;
        private readonly IRealtimeNotifier _realtime;

        public GroupCallService(
            IUnitOfWork unitOfWork,
            IChatService chat,
            PeopleDirectory people,
            IPresenceTracker presence,
            CallRegistry registry,
            IRealtimeNotifier realtime)
        {
            _unitOfWork = unitOfWork;
            _chat = chat;
            _people = people;
            _presence = presence;
            _registry = registry;
            _realtime = realtime;
        }

        public async Task<GroupCallDto> StartAsync(Guid userId, string connectionId, Guid conversationId, bool video)
        {
            var members = await RequireMemberAsync(conversationId, userId);

            var group = (await _unitOfWork.Repository<Conversation>().SelectAsync(
                c => c.Id == conversationId, c => new { c.IsGroup, c.Name })).First();
            if (!group.IsGroup)
                throw new ApiException("Only a group chat can have a group call.", 400);

            // Starting rings everyone, so it must not be free to repeat. Joining rings nobody.
            var now = DateTime.UtcNow;
            var isNew = _registry.GetGroupsIn(new[] { conversationId }).Count == 0;
            if (isNew && !_registry.TryCountAttempt(userId, now))
                throw new ApiException("You are calling too often. Wait a minute and try again.", 429);

            var entered = _registry.StartOrJoinGroup(conversationId, userId, connectionId, video, now);
            var call = await ToDtoAsync(Entered(entered));

            if (entered.Entry == GroupCallEntry.Started)
            {
                await _realtime.NotifyUsersAsync(members, Events.Started, call);
                await RingAsync(call, group.Name ?? string.Empty, members, now);
            }
            else
            {
                await _realtime.NotifyUsersAsync(members, Events.Joined, Change(call, userId));
            }

            return call;
        }

        public async Task<GroupCallDto> JoinAsync(Guid userId, string connectionId, Guid callId)
        {
            var going = _registry.GetGroup(callId) ?? throw new ApiException("That call is over.", 409);
            var members = await RequireMemberAsync(going.ConversationId, userId);

            var call = await ToDtoAsync(Entered(_registry.JoinGroup(callId, userId, connectionId, DateTime.UtcNow)));

            await _realtime.NotifyUsersAsync(members, Events.Joined, Change(call, userId));
            return call;
        }

        public Task LeaveAsync(Guid userId, Guid callId) =>
            AfterLeavingAsync(_registry.LeaveGroup(callId, userId, DateTime.UtcNow));

        public Task LeaveForConnectionAsync(string connectionId) =>
            AfterLeavingAsync(_registry.LeaveGroupByConnection(connectionId, DateTime.UtcNow));

        public Task LeaveForMemberAsync(Guid conversationId, Guid userId) =>
            AfterLeavingAsync(_registry.LeaveGroupOf(conversationId, userId, DateTime.UtcNow));

        public async Task DismissAsync(Guid userId, Guid callId)
        {
            var call = _registry.GetGroup(callId);
            if (call == null || !(await _chat.GetParticipantsAsync(call.ConversationId)).Contains(userId)) return;

            await _realtime.NotifyUserAsync(userId, Events.Dismissed,
                new GroupCallRefDto { CallId = call.Id, ConversationId = call.ConversationId });
        }

        public string? SignalTargetOf(Guid callId, Guid fromUserId, string fromConnectionId, Guid toUserId) =>
            _registry.GroupSeatOf(callId, fromUserId, fromConnectionId, toUserId);

        public async Task<List<GroupCallDto>> GetActiveAsync(Guid userId)
        {
            var chatIds = await _unitOfWork.Repository<ConversationParticipant>().SelectAsync(
                p => p.UserId == userId && p.Conversation.IsGroup, p => p.ConversationId);

            var calls = _registry.GetGroupsIn(chatIds).Values.OrderBy(c => c.StartedAt).ToList();
            if (calls.Count == 0) return new List<GroupCallDto>();

            var people = await NamesAsync(calls.SelectMany(c => c.Seats).Select(s => s.UserId));
            return calls.Select(call => ToDto(call, people)).ToList();
        }

        /// <summary>A call with the names of the people in it. Someone whose name is unknown is still listed.</summary>
        public static GroupCallDto ToDto(GroupCall call, IReadOnlyDictionary<Guid, ChatUserDto> people) => new()
        {
            CallId = call.Id,
            ConversationId = call.ConversationId,
            Video = call.Video,
            StartedByUserId = call.StartedByUserId,
            StartedAt = call.StartedAt,
            Participants = call.Seats.Select(seat =>
            {
                var person = people.GetValueOrDefault(seat.UserId);
                return new GroupCallParticipantDto
                {
                    Id = seat.UserId,
                    FullName = person?.FullName ?? string.Empty,
                    ProfilePictureUrl = person?.ProfilePictureUrl,
                    JoinedAt = seat.JoinedAt
                };
            }).ToList()
        };

        // ── helpers ───────────────────────────────────────────────────────────

        /// <summary>Everyone in the chat, once it is clear the person is one of them.</summary>
        private async Task<List<Guid>> RequireMemberAsync(Guid conversationId, Guid userId)
        {
            var members = await _chat.GetParticipantsAsync(conversationId);
            if (members.Count == 0)
                throw new ApiException("Chat not found.", 404);
            if (!members.Contains(userId))
                throw new ApiException("You are not in this chat.", 403);

            return members;
        }

        private static GroupCall Entered(GroupCallEntered entered) => entered.Entry switch
        {
            GroupCallEntry.Busy => throw new ApiException("You are already on a call.", 409),
            GroupCallEntry.Full => throw new ApiException($"The call is full ({GroupCall.MaxPeople} people).", 409),
            GroupCallEntry.Gone => throw new ApiException("That call is over.", 409),
            _ => entered.Call!
        };

        /// <summary>
        /// Rings the members who can pick up right now: online, not on a call,
        /// and with no block between them and the one who started it.
        /// </summary>
        private async Task RingAsync(GroupCallDto call, string groupName, List<Guid> members, DateTime now)
        {
            var starterId = call.StartedByUserId;
            var blocked = await _people.GetBlockedIdsAsync(starterId);

            var toRing = members
                .Where(id => id != starterId && _presence.IsOnline(id) && !blocked.Contains(id) && !_registry.IsBusy(id, now))
                .ToList();
            if (toRing.Count == 0) return;

            var starter = call.Participants.First(p => p.Id == starterId);
            await _realtime.NotifyUsersAsync(toRing, Events.Incoming, new GroupCallInviteDto
            {
                CallId = call.CallId,
                ConversationId = call.ConversationId,
                Video = call.Video,
                GroupName = groupName,
                From = new ChatPersonDto
                {
                    Id = starter.Id,
                    FullName = starter.FullName,
                    ProfilePictureUrl = starter.ProfilePictureUrl,
                    IsOnline = true
                }
            });
        }

        /// <summary>Tells the chat that someone left, and when they were the last, ends the call and writes it into the chat.</summary>
        private async Task AfterLeavingAsync(GroupCallLeft? left)
        {
            if (left == null) return;

            var call = await ToDtoAsync(left.Call);
            var members = await _chat.GetParticipantsAsync(call.ConversationId);

            // The one who left hears it too, also when they are no longer in the chat.
            await _realtime.NotifyUsersAsync(members.Append(left.UserId), Events.Left, Change(call, left.UserId));
            if (!left.Ended) return;

            await _realtime.NotifyUsersAsync(members, Events.Ended,
                new GroupCallRefDto { CallId = call.CallId, ConversationId = call.ConversationId });
            await _chat.LogGroupCallAsync(call.ConversationId, call.StartedByUserId, call.Video, left.Seconds, left.TookPart);
        }

        private static GroupCallChangeDto Change(GroupCallDto call, Guid userId) => new()
        {
            CallId = call.CallId,
            ConversationId = call.ConversationId,
            UserId = userId,
            Participants = call.Participants
        };

        private async Task<GroupCallDto> ToDtoAsync(GroupCall call) =>
            ToDto(call, await NamesAsync(call.Seats.Select(s => s.UserId)));

        private async Task<Dictionary<Guid, ChatUserDto>> NamesAsync(IEnumerable<Guid> userIds)
        {
            var ids = userIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<Guid, ChatUserDto>();

            var people = await _unitOfWork.Repository<User>().SelectAsync(
                u => ids.Contains(u.Id),
                u => new ChatUserDto { Id = u.Id, FullName = u.FullName, ProfilePictureUrl = u.ProfilePictureUrl });

            return people.ToDictionary(p => p.Id);
        }
    }
}
