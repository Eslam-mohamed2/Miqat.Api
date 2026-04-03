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
        Task<bool> DeleteAsync(Guid id);
    }
}
