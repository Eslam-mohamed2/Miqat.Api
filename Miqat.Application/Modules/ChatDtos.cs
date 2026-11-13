using Miqat.Domain.Enumerations;

namespace Miqat.Application.Modules;

/// <summary>One chat as the chat list shows it: who is in it, the newest message, what is unread.</summary>
public class ConversationDto
{
    public Guid Id { get; set; }

    public bool IsGroup { get; set; }

    /// <summary>The name of a group. Null for a chat between two people.</summary>
    public string? Name { get; set; }

    /// <summary>Who created the group. Null for a chat between two people.</summary>
    public Guid? CreatedByUserId { get; set; }

    /// <summary>The caller is an admin of this group.</summary>
    public bool IsAdmin { get; set; }

    /// <summary>Everyone in the chat, the caller too; the one who has been in it longest first.</summary>
    public List<ChatParticipantDto> Participants { get; set; } = new();

    /// <summary>The group call going on in this chat right now, if any.</summary>
    public GroupCallDto? ActiveCall { get; set; }

    /// <summary>
    /// The other person of a chat between two. For a group it is a stand-in
    /// (the chat's id and the group's name), so an app that only knows chats
    /// between two people still has something to show.
    /// </summary>
    public ChatPersonDto Friend { get; set; } = new();

    public ChatMessageDto? LastMessage { get; set; }
    public DateTime? LastMessageAt { get; set; }

    /// <summary>Messages from others that the caller has not read.</summary>
    public int UnreadCount { get; set; }

    /// <summary>
    /// How far the friend has read: own messages sent up to this moment are
    /// "seen". Null for a group, where every participant has a read mark of their own.
    /// </summary>
    public DateTime? FriendLastReadAt { get; set; }

    /// <summary>
    /// False once the two are no longer friends: the history stays readable,
    /// but nothing new can be sent. Always true for a member of a group.
    /// </summary>
    public bool CanSend { get; set; }
}

public class ChatPersonDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? ProfilePictureUrl { get; set; }

    /// <summary>Has the app open right now.</summary>
    public bool IsOnline { get; set; }
}

/// <summary>A person as a message names them: who sent it, who was added.</summary>
public class ChatUserDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? ProfilePictureUrl { get; set; }
}

/// <summary>One person in a chat.</summary>
public class ChatParticipantDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? ProfilePictureUrl { get; set; }
    public bool IsOnline { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime JoinedAt { get; set; }

    /// <summary>This person has read everything sent up to this moment.</summary>
    public DateTime? LastReadAt { get; set; }
}

public class ChatMessageDto
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid SenderId { get; set; }
    public ChatUserDto Sender { get; set; } = new();

    /// <summary>Empty when the message was deleted.</summary>
    public string Body { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public bool IsDeleted { get; set; }

    /// <summary>
    /// Set when the message is the record of a call ("Completed", "Missed",
    /// "Declined"); the sender is then the caller and there is no text.
    /// </summary>
    public CallOutcome? CallOutcome { get; set; }
    public bool? CallVideo { get; set; }
    /// <summary>How long the call lasted, for a completed one.</summary>
    public int? CallSeconds { get; set; }
    /// <summary>How many different people took part. Only set for a group call.</summary>
    public int? CallParticipants { get; set; }

    /// <summary>
    /// Set when the message is a line about the group itself; the sender is
    /// then who did it, and the text is that line in plain English.
    /// </summary>
    public ChatGroupEvent? GroupEvent { get; set; }
    /// <summary>The person added or removed.</summary>
    public ChatUserDto? TargetUser { get; set; }
    /// <summary>The name the group was created with or renamed to.</summary>
    public string? GroupName { get; set; }

    /// <summary>
    /// The value the sender's app gave the message before it was saved, sent
    /// back unchanged so that app can replace its "sending..." copy. Only set
    /// on the answer to sending and on the live event of a new message.
    /// </summary>
    public string? ClientId { get; set; }
}

public class SendMessageDto
{
    public string Body { get; set; } = string.Empty;

    /// <summary>Optional, up to 64 characters: see <see cref="ChatMessageDto.ClientId"/>.</summary>
    public string? ClientId { get; set; }
}

public class EditMessageDto
{
    public string Body { get; set; } = string.Empty;
}

/// <summary>A new group: its name and the friends to start it with (the caller is added by the server).</summary>
public class CreateGroupDto
{
    public string Name { get; set; } = string.Empty;
    public List<Guid> MemberIds { get; set; } = new();
}

public class RenameGroupDto
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>Friends of the caller to add to a group.</summary>
public class AddMembersDto
{
    public List<Guid> UserIds { get; set; } = new();
}

/// <summary>A group as its live event carries it: the same for everyone who receives it.</summary>
public class ChatGroupDto
{
    public Guid ConversationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid? CreatedByUserId { get; set; }
    public List<ChatParticipantDto> Participants { get; set; } = new();
}

/// <summary>A page of a chat, oldest first, and whether there is more before it.</summary>
public class ChatMessagePageDto
{
    public List<ChatMessageDto> Messages { get; set; } = new();
    public bool HasMore { get; set; }
}

/// <summary>For the badge: chats with unread messages, and unread messages in total.</summary>
public class ChatUnreadDto
{
    public int Conversations { get; set; }
    public int Messages { get; set; }
}

/// <summary>What a browser needs to set up a call: the servers that help two devices find each other.</summary>
public class CallSetupDto
{
    public List<IceServerDto> IceServers { get; set; } = new();

    /// <summary>How long a call rings before it counts as not answered.</summary>
    public int RingSeconds { get; set; }
}

/// <summary>One entry of RTCConfiguration.iceServers.</summary>
public class IceServerDto
{
    public List<string> Urls { get; set; } = new();
    public string? Username { get; set; }
    public string? Credential { get; set; }
}

/// <summary>A group call that is going on: who started it and who is in it right now.</summary>
public class GroupCallDto
{
    public Guid CallId { get; set; }
    public Guid ConversationId { get; set; }

    /// <summary>What the one who started it chose; everyone decides about their own camera.</summary>
    public bool Video { get; set; }

    public Guid StartedByUserId { get; set; }
    public DateTime StartedAt { get; set; }

    /// <summary>In the order they joined.</summary>
    public List<GroupCallParticipantDto> Participants { get; set; } = new();
}

public class GroupCallParticipantDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? ProfilePictureUrl { get; set; }
    public DateTime JoinedAt { get; set; }
}

/// <summary>The ringing invitation to a group call ("groupCallIncoming").</summary>
public class GroupCallInviteDto
{
    public Guid CallId { get; set; }
    public Guid ConversationId { get; set; }
    public bool Video { get; set; }
    public string GroupName { get; set; } = string.Empty;

    /// <summary>Who started the call.</summary>
    public ChatPersonDto From { get; set; } = new();
}

/// <summary>Someone joined or left a group call ("groupCallJoined", "groupCallLeft"): who, and who is in it now.</summary>
public class GroupCallChangeDto
{
    public Guid CallId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid UserId { get; set; }
    public List<GroupCallParticipantDto> Participants { get; set; } = new();
}

/// <summary>Which call an event is about ("groupCallEnded", "groupCallDismissed").</summary>
public class GroupCallRefDto
{
    public Guid CallId { get; set; }
    public Guid ConversationId { get; set; }
}
