using Miqat.Application.Modules;

namespace Miqat.Application.Interfaces
{
    public interface IUserService
    {
        Task<IEnumerable<UserDto>> GetAllUsers();

        /// <param name="includePrivateFields">
        /// false hides phone number and date of birth (someone else's profile).
        /// </param>
        Task<UserDto?> GetUserById(Guid id, bool includePrivateFields = true);

        Task<UserDto> CreateAsync(UserDto dto);

        /// <summary>Admin edit of any user. A blank Email keeps the current one.</summary>
        Task<bool> UpdateAsync(Guid id, UserDto dto);

        /// <summary>
        /// Self-service profile edit. Never touches Email, Role or ProfilePictureUrl.
        /// </summary>
        Task<bool> UpdateProfileAsync(Guid id, UpdateProfileDto dto);

        /// <summary>Sets the profile picture; null removes it.</summary>
        Task<bool> SetProfilePictureAsync(Guid id, string? imageUrl);

        /// <summary>The user's notification settings; null when the user is gone.</summary>
        Task<UserPreferencesDto?> GetPreferencesAsync(Guid id);

        /// <summary>Changes the settings that are sent; omitted ones keep their value.</summary>
        Task<UserPreferencesDto?> UpdatePreferencesAsync(Guid id, UserPreferencesDto dto);

        /// <summary>Admin only: activate / deactivate an account or change its role.</summary>
        Task<bool> SetAccessAsync(Guid id, UserAccessDto dto, Guid actingUserId);

        Task<bool> DeleteAsync(Guid id, Guid actingUserId);
        Task<IEnumerable<UserDto>> SearchAsync(string query, Guid excludeUserId);
    }
}
