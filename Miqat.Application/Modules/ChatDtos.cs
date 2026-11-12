using Miqat.Domain.Enumerations;

namespace Miqat.Application.Modules;

/// <summary>One chat as the chat list shows it: who it is with, the newest message, what is unread.</summary>
public class ConversationDto
{
    public Guid Id { get; set; }

    /// <summary>The other person of the chat.</summary>
    public ChatPersonDto Friend { get; set; } = new();

    public ChatMessageDto? LastMessage { get; set; }
    public DateTime? LastMessageAt { get; set; }

    /// <summary>Messages from the friend that the caller has not read.</summary>
    public int UnreadCount { get; set; }

    /// <summary>How far the friend has read: own messages sent up to this moment are "seen".</summary>
    public DateTime? FriendLastReadAt { get; set; }

    /// <summary>False once the two are no longer friends: the history stays readable, but nothing new can be sent.</summary>
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

public class ChatMessageDto
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public Guid SenderId { get; set; }

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
