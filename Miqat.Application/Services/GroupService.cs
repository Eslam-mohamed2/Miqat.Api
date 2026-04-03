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

        public async Task<IEnumerable<GroupDto>> GetAllGroups()
        {
            var groups = await _unitOfWork.Repository<Group>().GetAllAsync();
            return _mapper.MapToDtos(groups);
        }

        public async Task<GroupDto?> GetGroupById(Guid id)
        {
            var spec = new GroupByIdWithMembersSpec(id);
            var group = await _unitOfWork.Repository<Group>()
                .GetEntityWithSpec(spec);
            return group != null ? _mapper.MapToDto(group) : null;
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
    }
}
