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

    // The private Map*Name methods below are null-safe but marked
    // [UserMapping(Default = false)], which tells Mapperly not to apply them on its
    // own — so OwnerName, AssignedToUserName and GroupName were never populated and
    // came back null on every task. MapPropertyFromSource wires each one up
    // explicitly.
    //
    // Nested-path flattening (User.FullName) is NOT usable here: TaskItem declares
    // User as non-nullable, so Mapperly emits an unguarded task.User.FullName, which
    // throws on a freshly created entity whose navigation properties are not loaded.
    [MapPropertyFromSource(nameof(TaskDto.OwnerName), Use = nameof(MapOwnerName))]
    [MapPropertyFromSource(nameof(TaskDto.AssignedToUserName), Use = nameof(MapAssignedToUserName))]
    [MapPropertyFromSource(nameof(TaskDto.GroupName), Use = nameof(MapGroupName))]
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