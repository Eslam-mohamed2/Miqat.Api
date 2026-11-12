namespace Miqat.Application.Modules;

public class FriendshipStatusDto
{
    /// <summary>None | Sent | Pending | Friends | Blocked</summary>
    public string Status { get; set; } = "None";

    /// <summary>Id of the friendship row, when there is one the caller can act on.</summary>
    public Guid? Id { get; set; }
}
