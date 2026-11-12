using Miqat.Application.Modules;

namespace Miqat.Application.Interfaces
{
    /// <summary>
    /// Read-only views that pull several modules together around one person.
    /// </summary>
    public interface IUserOverviewService
    {
        /// <summary>Counts across tasks, projects, friends, notifications and mentions.</summary>
        Task<UserSummaryDto> GetSummaryAsync(Guid userId);

        /// <summary>Null when the person does not exist.</summary>
        Task<UserProfileDto?> GetProfileAsync(Guid viewerId, Guid userId, bool viewerIsAdmin);

        /// <summary>Friends and project teammates of the user.</summary>
        Task<IEnumerable<ContactDto>> GetContactsAsync(Guid userId);
    }
}
