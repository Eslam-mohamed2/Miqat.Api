using Miqat.Application.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Interfaces
{
    public interface IUserService
    {
        Task<IEnumerable<UserDto>> GetAllUsers();
        Task<UserDto?> GetUserById(Guid id);
        Task<UserDto> CreateAsync(UserDto dto);
        Task<bool> UpdateAsync(Guid id, UserDto dto);

        /// <summary>
        /// Self-service profile edit. Never touches Email, Role or ProfilePictureUrl.
        /// </summary>
        Task<bool> UpdateProfileAsync(Guid id, UpdateProfileDto dto);

        Task<bool> DeleteAsync(Guid id);
        Task<IEnumerable<UserDto>> SearchAsync(string query, Guid excludeUserId);
    }
}
