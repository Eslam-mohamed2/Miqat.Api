namespace Miqat.Application.Modules;

public class GroupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Color { get; set; }
    public Guid OwnerId { get; set; }
    public string? OwnerName { get; set; }
    public int MemberCount { get; set; }
    public int TaskCount { get; set; }

    /// <summary>Completed subset of <see cref="TaskCount"/>, for progress display.</summary>
    public int CompletedTaskCount { get; set; }
    public DateTime CreatedAt { get; set; }
}