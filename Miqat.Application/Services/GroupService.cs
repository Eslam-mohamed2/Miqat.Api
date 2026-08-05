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
        private readonly IAccessPolicy _access;
        private readonly ICurrentUserService _currentUser;
        private readonly IRealtimeNotifier _realtime;

        public GroupService(
            IUnitOfWork unitOfWork,
            GroupMapper mapper,
            IAccessPolicy access,
            ICurrentUserService currentUser,
            IRealtimeNotifier realtime)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _access = access;
            _currentUser = currentUser;
            _realtime = realtime;
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

            await _access.RequireAsync(
                _access.CanViewGroupAsync(id), "You do not have access to this project.");

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
            // Ownership comes from the token. The controller also sets this, but
            // relying on the payload alone would let a caller create a project
            // owned by somebody else.
            dto.OwnerId = _currentUser.RequireUserId();

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
            await _access.RequireAsync(
                _access.CanManageGroupAsync(id),
                "Only the project owner can change its details.");

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
            await _access.RequireAsync(
                _access.CanManageGroupAsync(id),
                "Only the project owner can delete it.");

            var entity = await _unitOfWork.Repository<Group>().GetByIdAsync(id);
            if (entity == null) return false;
            entity.SoftDelete();
            _unitOfWork.Repository<Group>().Update(entity);
            return await _unitOfWork.CompleteAsync() > 0;
        }

        public async Task<bool> AddMemberAsync(Guid groupId, Guid userId, Guid? addedByUserId = null)
        {
            await _access.RequireAsync(
                _access.CanManageGroupAsync(groupId),
                "Only the project owner can add members.");

            var existing = await _unitOfWork.Repository<GroupMember>()
                .FindAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

            if (existing.Any()) return false;

            var member = new GroupMember(groupId, userId);
            await _unitOfWork.Repository<GroupMember>().AddAsync(member);

            if (await _unitOfWork.CompleteAsync() <= 0) return false;

            await NotifyMemberAddedAsync(groupId, userId, addedByUserId);
            return true;
        }

        /// <summary>
        /// Tells the new member they were added, the way Notion does when someone
        /// adds you to a page.
        ///
        /// Deliberately non-fatal: the membership row is already committed by the
        /// time this runs, so failing to notify must not turn a successful add
        /// into an error for the caller.
        /// </summary>
        private async Task NotifyMemberAddedAsync(Guid groupId, Guid userId, Guid? addedByUserId)
        {
            // Nobody needs telling that they added themselves.
            if (addedByUserId == userId) return;

            try
            {
                var group = await _unitOfWork.Repository<Group>().GetByIdAsync(groupId);
                if (group == null) return;

                var actorName = "Someone";
                if (addedByUserId.HasValue)
                {
                    var actor = await _unitOfWork.Repository<User>().GetByIdAsync(addedByUserId.Value);
                    if (!string.IsNullOrWhiteSpace(actor?.FullName))
                        actorName = actor!.FullName;
                }

                var notification = new Notification(
                    title: "Added to a project",
                    message: $"{actorName} added you to \"{group.Name}\".",
                    type: Domain.Enumerations.NotificationType.GroupInvite,
                    recipientUserId: userId,
                    triggeredByUserId: addedByUserId,
                    linkedEntityId: groupId,
                    linkedEntityType: "Group"
                );

                await _unitOfWork.Repository<Notification>().AddAsync(notification);
                await _unitOfWork.CompleteAsync();

                await _realtime.NotifyUserAsync(userId, "notification", new
                {
                    title = notification.Title,
                    message = notification.Message,
                    type = "GroupInvite",
                    linkedEntityId = groupId,
                    linkedEntityType = "Group",
                    triggeredByUserName = actorName
                });
            }
            catch
            {
                // Swallowed on purpose — see the summary above.
            }
        }

        public async Task<bool> RemoveMemberAsync(Guid groupId, Guid userId)
        {
            // The owner can remove anyone; a member may remove themselves (leave).
            if (_currentUser.UserId != userId)
            {
                await _access.RequireAsync(
                    _access.CanManageGroupAsync(groupId),
                    "Only the project owner can remove other members.");
            }

            var existing = await _unitOfWork.Repository<GroupMember>()
                .FindAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

            var member = existing.FirstOrDefault();
            if (member == null) return false;

            _unitOfWork.Repository<GroupMember>().Delete(member);
            return await _unitOfWork.CompleteAsync() > 0;
        }

        public async Task<ApiResponse<PagedResult<MemberDto>>> GetGroupMembersPaged(Guid groupId, int pageIndex = 0, int pageSize = 20)
        {
            await _access.RequireAsync(
                _access.CanViewGroupAsync(groupId), "You do not have access to this project.");

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
