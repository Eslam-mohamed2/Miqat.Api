using Miqat.Application.Modules;
using Miqat.Application.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Interfaces
{
    public interface IGroupService
    {
        Task<IEnumerable<GroupDto>> GetAllGroups(Guid userId);
        Task<GroupDto?> GetGroupById(Guid id);
        Task<ApiResponse<PagedResult<MemberDto>>> GetGroupMembersPaged(Guid groupId, int pageIndex = 0, int pageSize = 20);
        Task<GroupDto> CreateAsync(GroupDto dto);
        Task<bool> UpdateAsync(Guid id, GroupDto dto);
        Task<bool> DeleteAsync(Guid id);
        Task<bool> AddMemberAsync(Guid groupId, Guid userId);
        Task<bool> RemoveMemberAsync(Guid groupId, Guid userId);
    }
}
