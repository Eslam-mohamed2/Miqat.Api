namespace Miqat.Application.Modules;

/// <summary>
/// What a user may change about their own profile.
///
/// Deliberately narrower than UserDto: PUT /api/User/me used to accept a full
/// UserDto, which meant
///   a) FluentValidation demanded an Email the profile form never sends, so every
///      save failed with "Email is required", and
///   b) the service assigned entity.Email from it, so the endpoint could change a
///      user's login address as a side effect of editing their timezone.
///
/// Email, role and profile picture are intentionally absent — the picture is set
/// by the upload endpoint, and the other two are not self-service.
/// </summary>
public class UpdateProfileDto
{
    public string FullName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Country { get; set; }
    public string? TimeZone { get; set; }
    public string? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
}
