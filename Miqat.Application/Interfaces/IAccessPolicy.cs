using System;
using System.Threading.Tasks;

namespace Miqat.Application.Interfaces
{
    /// <summary>
    /// Answers "may the current caller do this?" for the entities that have an
    /// owner or a membership.
    ///
    /// Kept in one place so the rules are stated once. Previously each service
    /// simply loaded by id and acted, so any signed-in user could read, edit and
    /// delete anyone else's tasks and projects.
    /// </summary>
    public interface IAccessPolicy
    {
        /// <summary>True when the caller owns the group or belongs to it.</summary>
        Task<bool> CanViewGroupAsync(Guid groupId);

        /// <summary>Owner only (or an Admin). Renaming, deleting, managing members.</summary>
        Task<bool> CanManageGroupAsync(Guid groupId);

        /// <summary>
        /// A task is visible to its creator, its assignee, and anyone in the
        /// project it belongs to.
        /// </summary>
        Task<bool> CanViewTaskAsync(Guid taskId);

        /// <summary>
        /// Same as viewing, except that a project member may edit shared work.
        /// Deleting is restricted to the creator or the project owner.
        /// </summary>
        Task<bool> CanEditTaskAsync(Guid taskId);

        Task<bool> CanDeleteTaskAsync(Guid taskId);

        /// <summary>Throws a 403 when the check fails, so callers can stay terse.</summary>
        Task RequireAsync(Task<bool> check, string message);
    }
}
