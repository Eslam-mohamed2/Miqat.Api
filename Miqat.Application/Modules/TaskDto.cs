namespace Miqat.Application.Modules;

public class TaskDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public DateTime? DueDate { get; set; }
    public string? Tags { get; set; }
    public string? Recurrence { get; set; }
    public DateTime? RecurrenceEndDate { get; set; }
    public Guid UserId { get; set; }
    public string? OwnerName { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public string? AssignedToUserName { get; set; }
    public Guid? GroupId { get; set; }
    public string? GroupName { get; set; }
    public DateTime CreatedAt { get; set; }
}