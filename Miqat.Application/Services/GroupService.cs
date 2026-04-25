using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Specifications.Groups;
using Miqat.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Services
{
    public class GroupService : IGroupService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly GroupMapper _mapper;

        public GroupService(IUnitOfWork unitOfWork, GroupMapper mapper)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
        }

        public async Task<IEnumerable<GroupDto>> GetAllGroups(Guid userId)
        {
            // Return groups where user is owner or a member
            var owned = await _unitOfWork.Repository<Group>()
                .FindAsync(g => g.OwnerId == userId && !g.IsDeleted);

            var memberSpec = new Miqat.Application.Specifications.Groups.GroupIdsByUserIdSpec(userId);
            var memberGroupsIds = await _unitOfWork.Repository<GroupMember>().ListAsync(memberSpec);
            var memberGroupIdsSet = memberGroupsIds.Select(m => m.GroupId).ToHashSet();

            var groups = owned.ToList();
            // Add member groups that are not owned
            var additional = await _unitOfWork.Repository<Group>().FindAsync(g => memberGroupIdsSet.Contains(g.Id) && !g.IsDeleted);
            groups.AddRange(additional.Where(g => !groups.Any(og => og.Id == g.Id)));

            return _mapper.MapToDtos(groups);
        }

        public async Task<GroupDto?> GetGroupById(Guid id)
        {
            // Use lightweight spec to fetch group with owner only and avoid loading large collections.
            var spec = new GroupWithOwnerSpec();
            spec = new GroupWithOwnerSpec();
            // We still need to filter by id; create a specific spec to include owner and match id
            var groupSpec = new GroupByIdWithMembersSpec(id); // reuse existing spec for simplicity
            var group = await _unitOfWork.Repository<Group>()
                .GetEntityWithSpec(groupSpec);

            if (group == null) return null;

            // Instead of returning preloaded members/tasks, compute counts via repository COUNT queries
            var memberCountSpec = new GroupMembersByGroupIdSpec(group.Id);
            var taskCountSpec = new Miqat.Application.Specifications.Tasks.TasksByGroupSpec(group.Id);

            var memberCount = await _unitOfWork.Repository<GroupMember>().CountAsync(memberCountSpec);
            var taskCount = await _unitOfWork.Repository<TaskItem>().CountAsync(taskCountSpec);

            var dto = _mapper.MapToDto(group);
            dto.MemberCount = memberCount;
            dto.TaskCount = taskCount;
            return dto;
        }

        public async Task<GroupDto> CreateAsync(GroupDto dto)
        {
            var entity = new Group(
                dto.Name,
                dto.Description,
                dto.OwnerId,
                dto.Color
            );

            await _unitOfWork.Repository<Group>().AddAsync(entity);
            await _unitOfWork.CompleteAsync();
            return _mapper.MapToDto(entity);
        }

        public async Task<bool> UpdateAsync(Guid id, GroupDto dto)
        {
            var entity = await _unitOfWork.Repository<Group>().GetByIdAsync(id);
            if (entity == null) return false;

            entity.Name = dto.Name;
            entity.Description = dto.Description;
            entity.Color = dto.Color;

            entity.SetUpdated();
            _unitOfWork.Repository<Group>().Update(entity);
            return await _unitOfWork.CompleteAsync() > 0;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var entity = await _unitOfWork.Repository<Group>().GetByIdAsync(id);
            if (entity == null) return false;
            entity.SoftDelete();
            _unitOfWork.Repository<Group>().Update(entity);
            return await _unitOfWork.CompleteAsync() > 0;
        }

        public async Task<bool> AddMemberAsync(Guid groupId, Guid userId)
        {
            var existing = await _unitOfWork.Repository<GroupMember>()
                .FindAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

            if (existing.Any()) return false;

            var member = new GroupMember(groupId, userId);
            await _unitOfWork.Repository<GroupMember>().AddAsync(member);
            return await _unitOfWork.CompleteAsync() > 0;
        }

        public async Task<bool> RemoveMemberAsync(Guid groupId, Guid userId)
        {
            var existing = await _unitOfWork.Repository<GroupMember>()
                .FindAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

            var member = existing.FirstOrDefault();
            if (member == null) return false;

            _unitOfWork.Repository<GroupMember>().Delete(member);
            return await _unitOfWork.CompleteAsync() > 0;
        }

        public async Task<ApiResponse<PagedResult<MemberDto>>> GetGroupMembersPaged(Guid groupId, int pageIndex = 0, int pageSize = 20)
        {
            // Validate group exists
            var group = await _unitOfWork.Repository<Group>().GetByIdAsync(groupId);
            if (group == null) return ApiResponse<PagedResult<MemberDto>>.Fail("Group not found.");
            // Use specification for members with paging
            var spec = new Miqat.Application.Specifications.Groups.GroupMembersByGroupIdWithUsersSpec(groupId, pageIndex, pageSize);
            var members = await _unitOfWork.Repository<GroupMember>().ListAsync(spec);

            // Also get total count for pagination
            var countSpec = new Miqat.Application.Specifications.Groups.GroupMembersByGroupIdSpec(groupId);
            var total = await _unitOfWork.Repository<GroupMember>().CountAsync(countSpec);

            var dtos = members.Select(m => new MemberDto
            {
                UserId = m.UserId,
                FullName = m.User.FullName,
                Email = m.User.Email,
                ProfilePictureUrl = m.User.ProfilePictureUrl,
                JoinedAt = m.JoinedAt
            }).ToList();

            var paged = PagedResult<MemberDto>.Create(dtos, total, pageIndex, pageSize);
            return ApiResponse<PagedResult<MemberDto>>.Ok(paged);
        }
    }
}
