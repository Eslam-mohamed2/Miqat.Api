using Miqat.Application.Modules;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Interfaces
{
    public interface IGroupService
    {
        Task<IEnumerable<GroupDto>> GetAllGroups();
        Task<GroupDto?> GetGroupById(Guid id);
        Task<GroupDto> CreateAsync(GroupDto dto);
        Task<bool> UpdateAsync(Guid id, GroupDto dto);
        Task<bool> DeleteAsync(Guid id);
        Task<bool> AddMemberAsync(Guid groupId, Guid userId);
        Task<bool> RemoveMemberAsync(Guid groupId, Guid userId);
    }
}
