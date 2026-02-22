using Riok.Mapperly.Abstractions;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;

namespace Miqat.Application.Common;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.None)] 
public partial class TaskMapper
{
    [MapProperty(nameof(TaskItem.Status), nameof(TaskDto.Status))]
    public partial TaskDto MapToDto(TaskItem task);
    public partial IEnumerable<TaskDto> MapToDtos(IEnumerable<TaskItem> tasks);
    private string MapEnumToString(Miqat.Domain.Enumerations.TaskStatus status)
        => status.ToString();
}