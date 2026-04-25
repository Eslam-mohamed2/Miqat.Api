namespace Miqat.Application.Modules;

public class MemberDto
{
    public Guid UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public DateTime JoinedAt { get; set; }
}
