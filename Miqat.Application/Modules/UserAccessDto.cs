namespace Miqat.Application.Modules;

/// <summary>
/// Body of PUT /api/User/{id}/access (admin only). A field that is omitted
/// keeps its current value.
/// </summary>
public class UserAccessDto
{
    /// <summary>false blocks sign-in and ends the user's sessions.</summary>
    public bool? IsActive { get; set; }

    /// <summary>"Admin" or "User".</summary>
    public string? Role { get; set; }
}
