namespace Miqat.Application.Modules;

/// <summary>Settings > Notifications. Body and result of /api/User/me/preferences.</summary>
public class UserPreferencesDto
{
    /// <summary>Live pop-ups in the open app. Notifications still reach the feed when off.</summary>
    public bool? PushNotifications { get; set; }

    public bool? EmailNotifications { get; set; }
    public bool? WeeklySummary { get; set; }
}

/// <summary>
/// GET /api/User/me/summary - the numbers the dashboard and the top bar need,
/// in one call instead of one per module.
/// </summary>
public class UserSummaryDto
{
    public TaskCountsDto Tasks { get; set; } = new();

    public int Projects { get; set; }
    public int Friends { get; set; }
    public int PendingFriendRequests { get; set; }
    public int UnreadNotifications { get; set; }
    public int UnreadMentions { get; set; }
    public int Boards { get; set; }

    /// <summary>The time zone "today" and "this week" were calculated in (the user's own).</summary>
    public string TimeZone { get; set; } = "UTC";
}

/// <summary>Counts over the tasks the user created or is assigned to.</summary>
public class TaskCountsDto
{
    public int Total { get; set; }
    public int Pending { get; set; }
    public int InProgress { get; set; }
    public int OnHold { get; set; }
    public int Completed { get; set; }
    public int Cancelled { get; set; }

    /// <summary>Past the due date and still open.</summary>
    public int Overdue { get; set; }
    public int DueToday { get; set; }

    /// <summary>Open tasks due within the next 7 days (today included).</summary>
    public int DueThisWeek { get; set; }

    /// <summary>Completed in the last 7 days.</summary>
    public int CompletedThisWeek { get; set; }

    /// <summary>Created by someone else and assigned to the user.</summary>
    public int AssignedToMe { get; set; }
}

/// <summary>
/// GET /api/User/{id}/profile - a person as seen by the caller: who they are,
/// how the two are connected, and what they share.
/// </summary>
public class UserProfileDto
{
    public UserDto User { get; set; } = new();
    public bool IsMe { get; set; }

    /// <summary>None | Sent | Pending | Friends | Blocked, with the friendship id to act on.</summary>
    public FriendshipStatusDto Friendship { get; set; } = new();

    public int FriendsCount { get; set; }
    public int MutualFriendsCount { get; set; }
    public List<UserCardDto> MutualFriends { get; set; } = new();

    /// <summary>Projects both people are on.</summary>
    public List<SharedProjectDto> SharedProjects { get; set; } = new();
}

public class SharedProjectDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Color { get; set; }
    public Guid OwnerId { get; set; }

    /// <summary>Tasks in this project that the profile's owner created or is assigned to.</summary>
    public int TaskCount { get; set; }
}

/// <summary>
/// GET /api/User/contacts - the people the caller can pick as an assignee or a
/// project member: their friends and everyone on their projects.
/// </summary>
public class ContactDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public bool IsFriend { get; set; }
    public bool IsTeammate { get; set; }

    /// <summary>"Friend", "Teammate" or "Friend and teammate".</summary>
    public string Relationship { get; set; } = string.Empty;
}
