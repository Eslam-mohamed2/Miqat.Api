using Miqat.Domain.Common;
using Miqat.Domain.Enumerations;

namespace Miqat.Domain.Entities;

/// <summary>
/// A chat. Today every chat is between two friends ("direct"); the people in it
/// are rows of <see cref="ConversationParticipant"/>, so a chat with more people
/// needs no change to this table.
/// </summary>
public class Conversation : BaseEntity
{
    /// <summary>
    /// The two people of a direct chat as one value ("smaller id:larger id").
    /// Unique, so a pair can never end up with two chats.
    /// </summary>
    public string? DirectKey { get; private set; }

    /// <summary>When the newest message was sent. Null while the chat is still empty.</summary>
    public DateTime? LastMessageAt { get; private set; }

    public virtual ICollection<ConversationParticipant> Participants { get; private set; } = new List<ConversationParticipant>();
    public virtual ICollection<ChatMessage> Messages { get; private set; } = new List<ChatMessage>();

    private Conversation() { }

    /// <summary>A new, empty chat between two people. Its participant rows are created by the caller.</summary>
    public static Conversation Direct(Guid userId, Guid otherUserId)
    {
        if (userId == otherUserId)
            throw new ArgumentException("A chat needs two different people.", nameof(otherUserId));

        return new Conversation { DirectKey = DirectKeyFor(userId, otherUserId) };
    }

    /// <summary>The same value whichever of the two asks.</summary>
    public static string DirectKeyFor(Guid userId, Guid otherUserId)
    {
        var (first, second) = userId.CompareTo(otherUserId) < 0 ? (userId, otherUserId) : (otherUserId, userId);
        return $"{first:N}:{second:N}";
    }

    public void MessageSent(DateTime at)
    {
        if (LastMessageAt == null || at > LastMessageAt) LastMessageAt = at;
    }
}

/// <summary>One person in a chat, and how far they have read it.</summary>
public class ConversationParticipant : BaseEntity
{
    public Guid ConversationId { get; private set; }
    public virtual Conversation Conversation { get; private set; } = null!;

    public Guid UserId { get; private set; }
    public virtual User User { get; private set; } = null!;

    /// <summary>Everything sent up to this moment counts as read. Null: nothing read yet.</summary>
    public DateTime? LastReadAt { get; private set; }

    public ConversationParticipant(Guid conversationId, Guid userId)
    {
        ConversationId = conversationId;
        UserId = userId;
    }

    private ConversationParticipant() { }

    /// <summary>Moves the read mark forward; it never moves back.</summary>
    public bool ReadUpTo(DateTime at)
    {
        if (LastReadAt != null && at <= LastReadAt) return false;

        LastReadAt = at;
        return true;
    }
}

/// <summary>One message of a chat.</summary>
public class ChatMessage : BaseEntity
{
    public const int MaxBodyLength = 4000;

    public Guid ConversationId { get; private set; }
    public virtual Conversation Conversation { get; private set; } = null!;

    public Guid SenderId { get; private set; }
    public virtual User Sender { get; private set; } = null!;

    /// <summary>Empty once the message is deleted: the row stays so the chat shows "message deleted".</summary>
    public string Body { get; private set; } = string.Empty;

    public DateTime? EditedAt { get; private set; }

    // A call shows in the chat as a line of its own ("Video call, 2 min",
    // "Missed call"). Such a message has no text; its sender is the caller.
    public CallOutcome? CallOutcome { get; private set; }
    public bool? CallVideo { get; private set; }
    /// <summary>How long the two talked. 0 unless the call was answered.</summary>
    public int? CallSeconds { get; private set; }

    public bool IsCall => CallOutcome != null;

    public ChatMessage(Guid conversationId, Guid senderId, string body)
    {
        ConversationId = conversationId;
        SenderId = senderId;
        Body = RequireBody(body);
    }

    private ChatMessage() { }

    /// <summary>The line a finished (or missed, or declined) call leaves in the chat.</summary>
    public static ChatMessage ForCall(Guid conversationId, Guid callerId, bool video, CallOutcome outcome, int seconds) => new()
    {
        ConversationId = conversationId,
        SenderId = callerId,
        CallOutcome = outcome,
        CallVideo = video,
        CallSeconds = outcome == Enumerations.CallOutcome.Completed ? Math.Max(0, seconds) : 0
    };

    public void Edit(string body)
    {
        if (IsCall) throw new InvalidOperationException("A call cannot be edited.");
        if (IsDeleted) throw new InvalidOperationException("A deleted message cannot be edited.");

        var text = RequireBody(body);
        if (text == Body) return;

        Body = text;
        EditedAt = DateTime.UtcNow;
        SetUpdated();
    }

    /// <summary>Removes the text for everyone; the message keeps its place in the chat.</summary>
    public void Remove()
    {
        Body = string.Empty;
        SoftDelete();
    }

    // No parameter name on purpose: the API answers 400 with this text as it is.
    private static string RequireBody(string? body)
    {
        var text = body?.Trim();
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("A message cannot be empty.");
        if (text.Length > MaxBodyLength)
            throw new ArgumentException($"A message cannot be longer than {MaxBodyLength} characters.");

        return text;
    }
}
