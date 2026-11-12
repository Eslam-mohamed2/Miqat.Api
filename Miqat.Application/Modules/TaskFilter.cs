using Miqat.Domain.Enumerations;
using TaskStatus = Miqat.Domain.Enumerations.TaskStatus;

namespace Miqat.Application.Modules;

/// <summary>
/// Optional narrowing of "my tasks" (GET /api/Task). Every field is optional;
/// with none set the full list comes back, as before.
/// </summary>
public class TaskFilter
{
    public TaskStatus? Status { get; set; }
    public Priority? Priority { get; set; }

    /// <summary>Only tasks in this project.</summary>
    public Guid? GroupId { get; set; }

    /// <summary>Due on or after this instant (UTC).</summary>
    public DateTime? DueFrom { get; set; }

    /// <summary>Due on or before this instant (UTC).</summary>
    public DateTime? DueTo { get; set; }

    public bool IsEmpty =>
        Status == null && Priority == null && GroupId == null && DueFrom == null && DueTo == null;
}
