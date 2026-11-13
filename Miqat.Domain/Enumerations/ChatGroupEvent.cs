namespace Miqat.Domain.Enumerations;

/// <summary>Something that happened to a group chat, shown in the chat as a line of its own.</summary>
public enum ChatGroupEvent
{
    /// <summary>The group was created; the line carries its name.</summary>
    Created,

    /// <summary>The sender added the target person.</summary>
    MemberAdded,

    /// <summary>The sender (an admin) removed the target person.</summary>
    MemberRemoved,

    /// <summary>The sender left the group.</summary>
    MemberLeft,

    /// <summary>The sender gave the group a new name; the line carries it.</summary>
    Renamed
}
