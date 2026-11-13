using Miqat.Domain.Common;
using Miqat.Domain.Enumerations;

namespace Miqat.Domain.Entities;

/// <summary>
/// A chat: between two friends ("direct") or a named group of people. The
/// people in it are rows of <see cref="ConversationParticipant"/> either way.
/// </summary>
public class Conversation : BaseEntity
{
    public const int MaxNameLength = 60;

    /// <summary>How many people a group can hold, its creator included.</summary>
    public const int MaxGroupSize = 50;

    /// <summary>A group starts with its creator and at least this many others.</summary>
    public const int MinOthersInNewGroup = 2;

    /// <summary>
    /// The two people of a direct chat as one value ("smaller id:larger id").
    /// Unique, so a pair can never end up with two chats. Null for a group.
    /// </summary>
    public string? DirectKey { get; private set; }

    public bool IsGroup { get; private set; }

    /// <summary>The name of a group. Null for a direct chat.</summary>
    public string? Name { get; private set; }

    /// <summary>Who created the group. Null for a direct chat.</summary>
    public Guid? CreatedByUserId { get; private set; }

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

    /// <summary>A new, empty group. Its participant rows are created by the caller.</summary>
    public static Conversation Group(string name, Guid createdByUserId) => new()
    {
        IsGroup = true,
        Name = RequireName(name),
        CreatedByUserId = createdByUserId
    };

    /// <summary>The same value whichever of the two asks.</summary>
    public static string DirectKeyFor(Guid userId, Guid otherUserId)
    {
        var (first, second) = userId.CompareTo(otherUserId) < 0 ? (userId, otherUserId) : (otherUserId, userId);
        return $"{first:N}:{second:N}";
    }

    /// <summary>Gives a group a new name. False when it already has that name.</summary>
    public bool Rename(string name)
    {
        if (!IsGroup) throw new InvalidOperationException("Only a group chat has a name.");

        var text = RequireName(name);
        if (text == Name) return false;

        Name = text;
        SetUpdated();
        return true;
    }

    public void MessageSent(DateTime at)
    {
        if (LastMessageAt == null || at > LastMessageAt) LastMessageAt = at;
    }

    /// <summary>
    /// Who becomes admin when a group is left without one: the person who has
    /// been in it longest. Null while the group still has an admin, or is empty.
    /// </summary>
    public static ConversationParticipant? NextAdmin(IEnumerable<ConversationParticipant> remaining)
    {
        var people = remaining.ToList();
        return people.Any(p => p.IsAdmin)
            ? null
            : people.OrderBy(p => p.CreatedAt).ThenBy(p => p.Id).FirstOrDefault();
    }

    // No parameter name on purpose: the API answers 400 with this text as it is.
    private static string RequireName(string? name)
    {
        var text = name?.Trim();
        if (string.IsNullOrEmpty(text))
            throw new ArgumentException("A group needs a name.");
        if (text.Length > MaxNameLength)
            throw new ArgumentException($"A group name cannot be longer than {MaxNameLength} characters.");

        return text;
    }
}

/// <summary>One person in a chat, and how far they have read it. The row's creation time is when they joined.</summary>
public class ConversationParticipant : BaseEntity
{
    public Guid ConversationId { get; private set; }
    public virtual Conversation Conversation { get; private set; } = null!;

    public Guid UserId { get; private set; }
    public virtual User User { get; private set; } = null!;

    /// <summary>Everything sent up to this moment counts as read. Null: nothing read yet.</summary>
    public DateTime? LastReadAt { get; private set; }

    /// <summary>May rename the group, remove members and make others admin. Always false in a direct chat.</summary>
    public bool IsAdmin { get; private set; }

    public ConversationParticipant(Guid conversationId, Guid userId, bool isAdmin = false)
    {
        ConversationId = conversationId;
        UserId = userId;
        IsAdmin = isAdmin;
    }

    private ConversationParticipant() { }

    /// <summary>Moves the read mark forward; it never moves back.</summary>
    public bool ReadUpTo(DateTime at)
    {
        if (LastReadAt != null && at <= LastReadAt) return false;

        LastReadAt = at;
        return true;
    }

    /// <summary>False when the person already is an admin.</summary>
    public bool MakeAdmin()
    {
        if (IsAdmin) return false;

        IsAdmin = true;
        SetUpdated();
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
    /// <summary>How long the call lasted. 0 unless it was answered.</summary>
    public int? CallSeconds { get; private set; }
    /// <summary>How many different people took part in a group call. Null for a call between two.</summary>
    public int? CallParticipants { get; private set; }

    // What happens to a group shows as a line too ("Sara added Omar"). Its
    // sender is who did it, and its text is that sentence in plain English
    // for apps that do not know these lines.
    public ChatGroupEvent? GroupEvent { get; private set; }
    /// <summary>The person added or removed.</summary>
    public Guid? TargetUserId { get; private set; }
    public virtual User? TargetUser { get; private set; }
    /// <summary>The name a group was created with or renamed to.</summary>
    public string? GroupName { get; private set; }

    public bool IsCall => CallOutcome != null;
    public bool IsGroupEvent => GroupEvent != null;

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

    /// <summary>
    /// The line a group call leaves in the chat: missed when nobody but the
    /// one who started it ever joined.
    /// </summary>
    public static ChatMessage ForGroupCall(Guid conversationId, Guid starterId, bool video, int seconds, int tookPart)
    {
        var line = ForCall(conversationId, starterId, video,
            tookPart > 1 ? Enumerations.CallOutcome.Completed : Enumerations.CallOutcome.Missed, seconds);
        line.CallParticipants = Math.Max(1, tookPart);
        return line;
    }

    /// <summary>The line a change to a group leaves in the chat.</summary>
    public static ChatMessage ForGroupEvent(
        Guid conversationId, Guid actorId, ChatGroupEvent what, string sentence, Guid? targetUserId = null, string? groupName = null) => new()
    {
        ConversationId = conversationId,
        SenderId = actorId,
        GroupEvent = what,
        Body = sentence.Length > MaxBodyLength ? sentence[..MaxBodyLength] : sentence,
        TargetUserId = targetUserId,
        GroupName = groupName
    };

    public void Edit(string body)
    {
        if (IsCall) throw new InvalidOperationException("A call cannot be edited.");
        if (IsGroupEvent) throw new InvalidOperationException("A line about the group cannot be edited.");
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
