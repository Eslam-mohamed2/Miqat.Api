using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Services;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Tests;

/// <summary>
/// A handful of people, who is friends with whom, and the group chat and
/// group call services over in-memory rows. What the services push live and
/// write into a chat is recorded, so a test can say what everyone was told.
/// </summary>
internal sealed class ChatWorld
{
    public List<Conversation> Conversations { get; } = new();
    public List<ConversationParticipant> Participants { get; } = new();
    public List<ChatMessage> Messages { get; } = new();

    public RecordingRealtime Realtime { get; } = new();
    public RecordingGroupCalls CallsOfGroupChat { get; } = new();
    public FakePresence Presence { get; } = new();
    public CallRegistry Registry { get; } = new();
    public FakeChat Chat { get; }

    private readonly List<User> _users = new();
    private readonly List<Friendship> _friendships = new();
    private readonly FakeUnitOfWork _uow = new();

    public ChatWorld()
    {
        _uow.Register<User>(new FakeRepository<User>(_users, u => u.Id));
        _uow.Register<Friendship>(new FakeRepository<Friendship>(_friendships, f => f.Id));
        _uow.Register<Conversation>(new FakeRepository<Conversation>(Conversations, c => c.Id));
        _uow.Register<ConversationParticipant>(new FakeRepository<ConversationParticipant>(Participants, p => p.Id));
        _uow.Register<ChatMessage>(new FakeRepository<ChatMessage>(Messages, m => m.Id));
        Chat = new FakeChat(Participants);
    }

    public User Person(string name)
    {
        var user = new User(name, $"{name.ToLowerInvariant().Replace(' ', '.')}@example.com", DateTime.UtcNow.AddYears(-30), null, null);
        _users.Add(user);
        return user;
    }

    /// <summary>A new person who is a friend of everyone given.</summary>
    public User FriendOf(string name, params User[] friends)
    {
        var user = Person(name);
        foreach (var friend in friends) Befriend(user, friend);
        return user;
    }

    public void Befriend(User one, User other)
    {
        var friendship = new Friendship(one.Id, other.Id) { Sender = one, Receiver = other };
        friendship.Accept();
        _friendships.Add(friendship);
    }

    /// <summary>Blocking replaces the friendship, as it does in the app.</summary>
    public void Block(User blocker, User blocked)
    {
        var friendship = _friendships.FirstOrDefault(f =>
            (f.SenderId == blocker.Id && f.ReceiverId == blocked.Id) || (f.SenderId == blocked.Id && f.ReceiverId == blocker.Id));
        if (friendship == null)
        {
            friendship = new Friendship(blocker.Id, blocked.Id);
            _friendships.Add(friendship);
        }

        friendship.BlockBy(blocker.Id, blocked.Id);
        friendship.Sender = blocker;
        friendship.Receiver = blocked;
    }

    /// <summary>A chat between two, as opening it creates it.</summary>
    public Conversation DirectChat(User one, User other)
    {
        var chat = Conversation.Direct(one.Id, other.Id);
        Conversations.Add(chat);
        Participants.Add(new ConversationParticipant(chat.Id, one.Id));
        Participants.Add(new ConversationParticipant(chat.Id, other.Id));
        return chat;
    }

    /// <summary>The group chat service as this person calls it.</summary>
    public GroupChatService GroupsAs(User user) =>
        new(_uow, new FakeCurrentUser(user.Id), new PeopleDirectory(_uow), Chat, CallsOfGroupChat, Realtime);

    public GroupCallService GroupCalls() =>
        new(_uow, Chat, new PeopleDirectory(_uow), Presence, Registry, Realtime);

    public Conversation Group(Guid id) => Conversations.Single(c => c.Id == id);
    public List<ConversationParticipant> PeopleIn(Guid conversationId) => Participants.Where(p => p.ConversationId == conversationId).ToList();
    public ConversationParticipant Seat(Guid conversationId, User user) => Participants.Single(p => p.ConversationId == conversationId && p.UserId == user.Id);
    public List<ChatMessage> Lines(Guid conversationId) => Messages.Where(m => m.ConversationId == conversationId).ToList();
}

/// <summary>Remembers every live event: who it went to, its name and what it carried.</summary>
internal sealed class RecordingRealtime : IRealtimeNotifier
{
    public List<(HashSet<Guid> To, string Event, object Payload)> Sent { get; } = new();

    public Task NotifyUserAsync(Guid userId, string eventName, object payload) =>
        NotifyUsersAsync(new[] { userId }, eventName, payload);

    public Task NotifyUsersAsync(IEnumerable<Guid> userIds, string eventName, object payload)
    {
        Sent.Add((userIds.ToHashSet(), eventName, payload));
        return Task.CompletedTask;
    }

    /// <summary>Everyone who was sent this event, over all the times it was sent.</summary>
    public HashSet<Guid> Heard(string eventName) =>
        Sent.Where(s => s.Event == eventName).SelectMany(s => s.To).ToHashSet();

    public List<T> Payloads<T>(string eventName) =>
        Sent.Where(s => s.Event == eventName).Select(s => s.Payload).OfType<T>().ToList();
}

internal sealed class FakePresence : IPresenceTracker
{
    private readonly HashSet<Guid> _online = new();

    public void Online(params User[] users) => _online.UnionWith(users.Select(u => u.Id));

    public bool IsOnline(Guid userId) => _online.Contains(userId);
    public bool Connected(Guid userId) => _online.Add(userId);
    public bool Disconnected(Guid userId) => _online.Remove(userId);
}

/// <summary>The part of the chat the group services lean on: who is in a chat, and what they ask it to show.</summary>
internal sealed class FakeChat : IChatService
{
    private readonly List<ConversationParticipant> _participants;

    public FakeChat(List<ConversationParticipant> participants) => _participants = participants;

    /// <summary>Ids of the messages that were shown to their chat.</summary>
    public List<Guid> Announced { get; } = new();

    /// <summary>Chats whose members were told what the group looks like now.</summary>
    public List<Guid> GroupsAnnounced { get; } = new();

    public List<(Guid ConversationId, Guid StarterId, bool Video, int Seconds, IReadOnlyCollection<Guid> TookPart)> GroupCallsLogged { get; } = new();

    public Task<List<Guid>> GetParticipantsAsync(Guid conversationId) =>
        Task.FromResult(_participants.Where(p => p.ConversationId == conversationId).Select(p => p.UserId).ToList());

    public Task<ConversationDto?> GetConversationAsync(Guid conversationId) =>
        Task.FromResult<ConversationDto?>(new ConversationDto { Id = conversationId });

    public Task<ChatMessageDto?> AnnounceAsync(Guid messageId)
    {
        Announced.Add(messageId);
        return Task.FromResult<ChatMessageDto?>(new ChatMessageDto { Id = messageId });
    }

    public Task AnnounceGroupAsync(Guid conversationId)
    {
        GroupsAnnounced.Add(conversationId);
        return Task.CompletedTask;
    }

    public Task<ChatMessageDto?> LogGroupCallAsync(
        Guid conversationId, Guid starterId, bool video, int seconds, IReadOnlyCollection<Guid> tookPart)
    {
        GroupCallsLogged.Add((conversationId, starterId, video, seconds, tookPart));
        return Task.FromResult<ChatMessageDto?>(new ChatMessageDto { ConversationId = conversationId });
    }

    public Task<List<ConversationDto>> GetConversationsAsync() => throw new NotSupportedException();
    public Task<ConversationDto> OpenWithAsync(Guid friendId) => throw new NotSupportedException();
    public Task<ChatMessagePageDto?> GetMessagesAsync(Guid conversationId, Guid? beforeMessageId, int? limit) => throw new NotSupportedException();
    public Task<ChatMessageDto?> SendAsync(Guid conversationId, SendMessageDto dto) => throw new NotSupportedException();
    public Task<ChatMessageDto?> EditAsync(Guid messageId, EditMessageDto dto) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(Guid messageId) => throw new NotSupportedException();
    public Task<bool> MarkReadAsync(Guid conversationId) => throw new NotSupportedException();
    public Task<ChatUnreadDto> GetUnreadAsync() => throw new NotSupportedException();
    public Task<List<Guid>> GetOnlineFriendIdsAsync() => throw new NotSupportedException();
    public Task<List<Guid>> GetOthersInAsync(Guid conversationId, Guid userId) => throw new NotSupportedException();
    public Task<ChatMessageDto> LogCallAsync(Guid conversationId, Guid callerId, bool video, CallOutcome outcome, int seconds) => throw new NotSupportedException();
}

/// <summary>Remembers who was taken out of the call of a group chat.</summary>
internal sealed class RecordingGroupCalls : IGroupCallService
{
    public List<(Guid ConversationId, Guid UserId)> Left { get; } = new();

    public Task LeaveForMemberAsync(Guid conversationId, Guid userId)
    {
        Left.Add((conversationId, userId));
        return Task.CompletedTask;
    }

    public Task<GroupCallDto> StartAsync(Guid userId, string connectionId, Guid conversationId, bool video) => throw new NotSupportedException();
    public Task<GroupCallDto> JoinAsync(Guid userId, string connectionId, Guid callId) => throw new NotSupportedException();
    public Task LeaveAsync(Guid userId, Guid callId) => throw new NotSupportedException();
    public Task LeaveForConnectionAsync(string connectionId) => throw new NotSupportedException();
    public Task DismissAsync(Guid userId, Guid callId) => throw new NotSupportedException();
    public string? SignalTargetOf(Guid callId, Guid fromUserId, string fromConnectionId, Guid toUserId) => throw new NotSupportedException();
    public Task<List<GroupCallDto>> GetActiveAsync(Guid userId) => throw new NotSupportedException();
}
