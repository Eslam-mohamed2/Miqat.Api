using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using TaskStatus = Miqat.Domain.Enumerations.TaskStatus;

namespace Miqat.Application.Common;

/// <summary>
/// TaskItem -> TaskDto. Written out by hand: every related value (owner,
/// assignee, project) is read through a null-safe navigation, which is the
/// part a generated mapper kept getting wrong - a freshly created entity has
/// no navigations loaded.
///
/// CommentCount is not set here (it needs a query); see TaskDtoFactory.
/// </summary>
public class TaskMapper
{
    public TaskDto MapToDto(TaskItem task) => new()
    {
        Id = task.Id,
        Title = task.Title,
        Description = task.Description,
        Status = task.Status.ToString(),
        Priority = task.Priority.ToString(),
        StartDate = task.StartDate,
        DueDate = task.DueDate,
        Tags = task.Tags,
        Recurrence = task.Recurrence.ToString(),
        RecurrenceEndDate = task.RecurrenceEndDate,

        UserId = task.UserId,
        OwnerName = task.User?.FullName,
        OwnerProfilePictureUrl = task.User?.ProfilePictureUrl,

        AssignedToUserId = task.AssignedToUserId,
        AssignedToUserName = task.AssignedToUser?.FullName,
        AssignedToUserProfilePictureUrl = task.AssignedToUser?.ProfilePictureUrl,

        GroupId = task.GroupId,
        GroupName = task.Group?.Name,
        GroupColor = task.Group?.Color,

        IsOverdue = IsOverdue(task),
        CompletedAt = task.CompletedAt,
        CreatedAt = task.CreatedAt,
        UpdatedAt = task.UpdatedAt
    };

    public IEnumerable<TaskDto> MapToDtos(IEnumerable<TaskItem> tasks)
        => tasks.Select(MapToDto).ToList();

    /// <summary>Past its due date and neither finished nor cancelled.</summary>
    public static bool IsOverdue(TaskItem task) =>
        task.DueDate.HasValue &&
        task.DueDate.Value < DateTime.UtcNow &&
        task.Status != TaskStatus.Completed &&
        task.Status != TaskStatus.Cancelled;
}
