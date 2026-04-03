using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Miqat.Application.Common;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.None)]
public partial class TaskMapper
{
    [MapProperty(nameof(TaskItem.Status), nameof(TaskDto.Status))]
    [MapProperty(nameof(TaskItem.Priority), nameof(TaskDto.Priority))]
    [MapProperty(nameof(TaskItem.Recurrence), nameof(TaskDto.Recurrence))]

    // ← removed the nullable navigation MapProperty attributes
    // ← Mapperly will call our private methods below instead
    public partial TaskDto MapToDto(TaskItem task);

    public partial IEnumerable<TaskDto> MapToDtos(IEnumerable<TaskItem> tasks);

    // ── Enum converters ───────────────────────────────────────────────────────
    private string MapEnumToString(Miqat.Domain.Enumerations.TaskStatus status)
        => status.ToString();

    private string MapEnumToString(Miqat.Domain.Enumerations.Priority priority)
        => priority.ToString();

    private string? MapEnumToString(Miqat.Domain.Enumerations.RecurrencePattern recurrence)
        => recurrence.ToString();

    // ── Nullable navigation property handlers ─────────────────────────────────
    // Mapperly sees these and uses them instead of crashing on null

    [UserMapping(Default = false)]
    private string? MapOwnerName(TaskItem task)
        => task.User?.FullName;

    [UserMapping(Default = false)]
    private string? MapAssignedToUserName(TaskItem task)
        => task.AssignedToUser?.FullName;

    [UserMapping(Default = false)]
    private string? MapGroupName(TaskItem task)
        => task.Group?.Name;
}