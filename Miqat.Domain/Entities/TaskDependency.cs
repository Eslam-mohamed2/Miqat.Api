using Miqat.Domain.Common;

namespace Miqat.Domain.Entities;

/// <summary>"<see cref="Task"/> cannot start until <see cref="DependsOnTask"/> is finished."</summary>
public class TaskDependency : BaseEntity
{
    public Guid TaskId { get; private set; }
    public virtual TaskItem Task { get; private set; } = null!;

    public Guid DependsOnTaskId { get; private set; }
    public virtual TaskItem DependsOnTask { get; private set; } = null!;

    public TaskDependency(Guid taskId, Guid dependsOnTaskId)
    {
        if (taskId == dependsOnTaskId)
            throw new ArgumentException("A task cannot depend on itself.", nameof(dependsOnTaskId));

        TaskId = taskId;
        DependsOnTaskId = dependsOnTaskId;
    }

    private TaskDependency() { }
}
