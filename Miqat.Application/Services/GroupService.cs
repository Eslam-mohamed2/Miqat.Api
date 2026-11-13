using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Specifications.Groups;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;
using TaskStatus = Miqat.Domain.Enumerations.TaskStatus;

namespace Miqat.Application.Services
{
    public class GroupService : IGroupService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly GroupMapper _mapper;
        private readonly IAccessPolicy _access;
        private readonly ICurrentUserService _currentUser;
        private readonly INotificationSender _notifications;

        public GroupService(
            IUnitOfWork unitOfWork,
            GroupMapper mapper,
            IAccessPolicy access,
            ICurrentUserService currentUser,
            INotificationSender notifications)
        {
            _unitOfWork = unitOfWork;
            _mapper = mapper;
            _access = access;
            _currentUser = currentUser;
            _notifications = notifications;
        }

        public async Task<IEnumerable<GroupDto>> GetAllGroups(Guid userId)
        {
            // The projects the user owns or is a member of, in one query.
            var groups = await _unitOfWork.Repository<Group>()
                .FindAsync(g => !g.IsDeleted && (g.OwnerId == userId || g.Members.Any(m => m.UserId == userId)));

            return await AttachCountsAsync(groups.OrderByDescending(g => g.CreatedAt).ToList());
        }

        /// <summary>
        /// Fills in owner name plus member and task counts for a list of projects.
        /// </summary>
        /// <remarks>
        /// These were read off <c>group.Members</c> / <c>group.Tasks</c>, but the
        /// list query uses FindAsync, which loads neither — so every project card
        /// reported 0 members and 0 tasks.
        ///
        /// A fixed number of grouped queries, regardless of how many projects the
        /// user has.
        /// </remarks>
        private async Task<List<GroupDto>> AttachCountsAsync(IList<Group> groups)
        {
            var dtos = groups.Select(_mapper.MapToDto).ToList();
            if (dtos.Count == 0) return dtos;

            var ids = dtos.Select(d => d.Id).ToList();

            // The membership rows answer two questions at once: how many members
            // each project has, and (further down) whether its owner is among them.
            var memberRows = await _unitOfWork.Repository<GroupMember>().SelectAsync(
                gm => ids.Contains(gm.GroupId) && !gm.IsDeleted, gm => new { gm.GroupId, gm.UserId });
            var memberCounts = memberRows.GroupBy(gm => gm.GroupId).ToDictionary(g => g.Key, g => g.Count());

            var taskCounts = await _unitOfWork.Repository<TaskItem>()
                .CountGroupedAsync(
                    t => t.GroupId.HasValue && ids.Contains(t.GroupId.Value) && !t.IsDeleted,
                    t => t.GroupId!.Value);

            var doneCounts = await _unitOfWork.Repository<TaskItem>()
                .CountGroupedAsync(
                    t => t.GroupId.HasValue && ids.Contains(t.GroupId.Value) && !t.IsDeleted
                         && t.Status == TaskStatus.Completed,
                    t => t.GroupId!.Value);

            var commentCounts = await _unitOfWork.Repository<GroupComment>()
                .CountGroupedAsync(c => ids.Contains(c.GroupId) && !c.IsDeleted, c => c.GroupId);

            // OwnerName has the same problem the counts had: the mapper reads
            // group.Owner?.FullName, and FindAsync loads no navigations.
            var ownerIds = groups.Select(g => g.OwnerId).Distinct().ToList();
            var owners = (await _unitOfWork.Repository<User>()
                    .FindAsync(u => ownerIds.Contains(u.Id)))
                .ToDictionary(u => u.Id);

            var callerId = _currentUser.UserId;

            // Which owners already have a GroupMember row, so the +1 below is
            // applied only where it is actually missing. Seeded projects have one;
            // projects created through the app do not.
            var ownerById = groups.ToDictionary(g => g.Id, g => g.OwnerId);
            var ownerAlreadyMember = memberRows
                .Where(gm => ownerById.TryGetValue(gm.GroupId, out var o) && o == gm.UserId)
                .Select(gm => gm.GroupId)
                .ToHashSet();

            foreach (var dto in dtos)
            {
                if (owners.TryGetValue(dto.OwnerId, out var owner))
                {
                    if (string.IsNullOrWhiteSpace(dto.OwnerName)) dto.OwnerName = owner.FullName;
                    dto.OwnerProfilePictureUrl = owner.ProfilePictureUrl;
                }

                dto.IsOwner = callerId.HasValue && callerId.Value == dto.OwnerId;

                // Counts what the members list shows, owner included — see
                // GetGroupMembersPaged.
                var members = memberCounts.TryGetValue(dto.Id, out var m) ? m : 0;
                dto.MemberCount = members + (ownerAlreadyMember.Contains(dto.Id) ? 0 : 1);
                dto.TaskCount = taskCounts.TryGetValue(dto.Id, out var t) ? t : 0;
                dto.CompletedTaskCount = doneCounts.TryGetValue(dto.Id, out var d) ? d : 0;
                dto.CommentCount = commentCounts.TryGetValue(dto.Id, out var c) ? c : 0;
            }

            return dtos;
        }

        public async Task<GroupDto?> GetGroupById(Guid id)
        {
            var group = await FindLiveGroupAsync(id);
            if (group == null) return null;

            await _access.RequireAsync(
                _access.CanViewGroupAsync(id), "You do not have access to this project.");

            // Same counting rules as the list endpoint, without loading the
            // project's whole member and task collections just to count them.
            return (await AttachCountsAsync(new List<Group> { group })).First();
        }

        public async Task<GroupDto> CreateAsync(GroupDto dto)
        {
            // Ownership comes from the token. Relying on the payload alone would
            // let a caller create a project owned by somebody else.
            var ownerId = _currentUser.RequireUserId();

            var entity = new Group(dto.Name.Trim(), dto.Description, ownerId, dto.Color);

            await _unitOfWork.Repository<Group>().AddAsync(entity);
            await _unitOfWork.CompleteAsync();

            // Through AttachCountsAsync so the response has the owner's name and
            // the same counts (1 member: the owner) the list will show.
            return (await AttachCountsAsync(new List<Group> { entity })).First();
        }

        public async Task<bool> UpdateAsync(Guid id, GroupDto dto)
        {
            var entity = await FindLiveGroupAsync(id);
            if (entity == null) return false;

            await _access.RequireAsync(
                _access.CanManageGroupAsync(id),
                "Only the project owner can change its details.");

            entity.Name = dto.Name.Trim();
            entity.Description = dto.Description;
            entity.Color = dto.Color;

            entity.SetUpdated(_currentUser.UserId?.ToString());
            await _unitOfWork.CompleteAsync();
            return true;
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var entity = await FindLiveGroupAsync(id);
            if (entity == null) return false;

            await _access.RequireAsync(
                _access.CanManageGroupAsync(id),
                "Only the project owner can delete it.");

            entity.SoftDelete(_currentUser.UserId?.ToString());
            await _unitOfWork.CompleteAsync();
            return true;
        }

        public async Task<bool> AddMemberAsync(Guid groupId, Guid userId, Guid? addedByUserId = null)
        {
            var group = await FindLiveGroupAsync(groupId)
                ?? throw new ApiException("Project not found.", 404);

            await _access.RequireAsync(
                _access.CanManageGroupAsync(groupId),
                "Only the project owner can add members.");

            // Without this an unknown id reached the database and came back as a 500.
            var user = await _unitOfWork.Repository<User>().GetByIdAsync(userId);
            if (user == null || user.IsDeleted || !user.IsActive)
                throw new ApiException("User not found.", 404);

            // The owner is on the team by definition.
            if (group.OwnerId == userId) return false;

            var existing = await _unitOfWork.Repository<GroupMember>()
                .FindAsync(gm => gm.GroupId == groupId && gm.UserId == userId);

            if (existing.Any()) return false;

            await _unitOfWork.Repository<GroupMember>().AddAsync(new GroupMember(groupId, userId));
            await _unitOfWork.CompleteAsync();

            await NotifyMemberAddedAsync(group, userId, addedByUserId ?? _currentUser.UserId);
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
        private Task NotifyMemberAddedAsync(Group group, Guid userId, Guid? addedByUserId) =>
            _notifications.SendAsync(
                new NotificationContent(
                    NotificationType.GroupInvite, "Added to a project",
                    actor => $"{actor} added you to \"{group.Name}\".",
                    addedByUserId, group.Id, "Group"),
                new[] { userId });

        public async Task<bool> RemoveMemberAsync(Guid groupId, Guid userId)
        {
            if (await FindLiveGroupAsync(groupId) == null)
                throw new ApiException("Project not found.", 404);

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
            await _unitOfWork.CompleteAsync();
            return true;
        }

        public async Task<ApiResponse<PagedResult<MemberDto>>> GetGroupMembersPaged(Guid groupId, int pageIndex = 0, int pageSize = 20)
        {
            var group = await FindLiveGroupAsync(groupId);
            if (group == null) return ApiResponse<PagedResult<MemberDto>>.Fail("Group not found.");

            await _access.RequireAsync(
                _access.CanViewGroupAsync(groupId), "You do not have access to this project.");

            // A negative page index used to reach Skip() and fail as a 500.
            (pageIndex, pageSize) = Paging.Normalize(pageIndex, pageSize);

            var members = await _unitOfWork.Repository<GroupMember>()
                .ListAsync(new GroupMembersByGroupIdWithUsersSpec(groupId, pageIndex, pageSize));

            var total = await _unitOfWork.Repository<GroupMember>()
                .CountAsync(new GroupMembersByGroupIdSpec(groupId));

            var dtos = members.Select(m => new MemberDto
            {
                UserId = m.UserId,
                FullName = m.User.FullName,
                Email = m.User.Email,
                ProfilePictureUrl = m.User.ProfilePictureUrl,
                JoinedAt = m.JoinedAt,
                IsOwner = m.UserId == group.OwnerId,
                Role = m.UserId == group.OwnerId ? "Owner" : "Member"
            }).ToList();

            // The owner belongs on the team list. Projects created through the
            // app give them no GroupMember row, so the list used to omit the one
            // person who certainly is on the project. Listed first, on the first
            // page only. Checked against the whole project, not just this page —
            // otherwise an owner whose row sits on a later page is counted twice.
            var ownerHasRow = (await _unitOfWork.Repository<GroupMember>()
                    .FindAsync(gm => gm.GroupId == groupId && gm.UserId == group.OwnerId))
                .Any();

            if (!ownerHasRow)
            {
                total += 1;
                if (pageIndex == 0)
                {
                    var owner = await _unitOfWork.Repository<User>().GetByIdAsync(group.OwnerId);
                    if (owner != null)
                    {
                        dtos.Insert(0, new MemberDto
                        {
                            UserId = owner.Id,
                            FullName = owner.FullName,
                            Email = owner.Email,
                            ProfilePictureUrl = owner.ProfilePictureUrl,
                            JoinedAt = group.CreatedAt,
                            IsOwner = true,
                            Role = "Owner"
                        });
                    }
                }
            }

            var paged = PagedResult<MemberDto>.Create(dtos, total, pageIndex, pageSize);
            return ApiResponse<PagedResult<MemberDto>>.Ok(paged);
        }

        private async Task<Group?> FindLiveGroupAsync(Guid id)
        {
            var group = await _unitOfWork.Repository<Group>().GetByIdAsync(id);
            return group == null || group.IsDeleted ? null : group;
        }
    }
}
