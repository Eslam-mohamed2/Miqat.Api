using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Specifications.Groups;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Services
{
    /// <inheritdoc />
    /// <remarks>
    /// Built like the comments on a task (<see cref="CommentService"/>): the same
    /// limits, the same mention and notification plumbing, the same "edit is the
    /// author's alone" rule. What differs is what a discussion needs and a task
    /// thread does not: replies, pages, and a live event that carries the comment
    /// itself, so an open discussion changes in place instead of reloading.
    /// </remarks>
    public class GroupCommentService : IGroupCommentService
    {
        /// <summary>Name of the live (SignalR) event an open discussion listens for.</summary>
        public const string RealtimeEvent = "projectCommentChanged";

        public const int DefaultPageSize = 30;
        public const int MaxPageSize = 100;
        public const int MaxClientIdLength = 64;
        private const int MaxMentionsPerComment = 50;

        private const string NoAccess = "You do not have access to this project.";

        private readonly IUnitOfWork _unitOfWork;
        private readonly IAccessPolicy _access;
        private readonly ICurrentUserService _currentUser;
        private readonly IRealtimeNotifier _realtime;
        private readonly INotificationSender _notifications;
        private readonly PeopleDirectory _people;

        public GroupCommentService(
            IUnitOfWork unitOfWork,
            IAccessPolicy access,
            ICurrentUserService currentUser,
            IRealtimeNotifier realtime,
            INotificationSender notifications,
            PeopleDirectory people)
        {
            _unitOfWork = unitOfWork;
            _access = access;
            _currentUser = currentUser;
            _realtime = realtime;
            _notifications = notifications;
            _people = people;
        }

        private IGenericRepository<GroupComment> Comments => _unitOfWork.Repository<GroupComment>();

        // ── Reading ───────────────────────────────────────────────────────────

        public async Task<GroupCommentPageDto> GetPageAsync(Guid groupId, Guid? beforeCommentId, int? limit)
        {
            var me = _currentUser.RequireUserId();
            await RequireProjectAsync(groupId);

            DateTime? before = null;
            if (beforeCommentId.HasValue)
            {
                // A comment that was deleted since the page was read still marks
                // its place, so scrolling up keeps working. A reply stands for the
                // comment it answers.
                var cursor = (await Comments.SelectAsync(
                        c => c.Id == beforeCommentId && c.GroupId == groupId,
                        c => new { c.ParentId, c.CreatedAt })).FirstOrDefault()
                    ?? throw new ApiException("That comment is not in this project.", 400);

                before = cursor.ParentId.HasValue
                    ? (await Comments.SelectAsync(c => c.Id == cursor.ParentId, c => (DateTime?)c.CreatedAt)).FirstOrDefault()
                      ?? cursor.CreatedAt
                    : cursor.CreatedAt;
            }

            var size = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
            var newestFirst = await Comments.ListAsync(new GroupCommentThreadsPageSpec(groupId, before, size + 1));
            var threads = newestFirst.Take(size).Reverse().ToList();

            var threadIds = threads.Select(c => c.Id).ToList();
            var replies = threadIds.Count == 0
                ? new List<GroupComment>()
                : (await Comments.FindAsync(c => c.ParentId != null && threadIds.Contains(c.ParentId.Value) && !c.IsDeleted))
                    .OrderBy(c => c.CreatedAt)
                    .ToList();

            var authors = await AuthorsAsync(threads.Concat(replies));
            var canManage = await _access.CanManageGroupAsync(groupId);
            var repliesByParent = replies.ToLookup(c => c.ParentId!.Value);

            var page = new GroupCommentPageDto
            {
                HasMore = newestFirst.Count > size,
                Total = await CountAsync(groupId)
            };

            foreach (var thread in threads)
            {
                page.Comments.Add(ToDto(thread, authors, me, canManage));
                page.Comments.AddRange(repliesByParent[thread.Id].Select(reply => ToDto(reply, authors, me, canManage)));
            }

            return page;
        }

        public async Task<IEnumerable<MentionableUserDto>> GetMentionableAsync(Guid groupId)
        {
            var me = _currentUser.RequireUserId();
            var group = await RequireProjectAsync(groupId);

            var ids = await _people.GetProjectAudienceAsync(groupId);
            ids.Remove(me);

            var users = await _unitOfWork.Repository<User>()
                .FindAsync(u => ids.Contains(u.Id) && !u.IsDeleted && u.IsActive);

            return users
                .Select(user => new MentionableUserDto
                {
                    UserId = user.Id,
                    FullName = user.FullName,
                    Email = user.Email,
                    ProfilePictureUrl = user.ProfilePictureUrl,
                    Relationship = user.Id == group.OwnerId ? "Project owner" : "Project member",
                    // Everyone listed is on the project already: a mention here never lets anyone in.
                    HasAccess = true
                })
                .OrderBy(u => u.FullName)
                .ToList();
        }

        // ── Writing ───────────────────────────────────────────────────────────

        public async Task<GroupCommentDto> AddAsync(Guid groupId, CreateGroupCommentDto dto)
        {
            var me = _currentUser.RequireUserId();
            var group = await RequireProjectAsync(groupId);

            var text = ValidateContent(dto.Content);

            var clientId = dto.ClientId?.Trim();
            if (clientId?.Length > MaxClientIdLength)
                throw new ApiException($"clientId cannot be longer than {MaxClientIdLength} characters.", 400);

            GroupComment? parent = null;
            if (dto.ParentId.HasValue)
            {
                parent = await Comments.GetByIdAsync(dto.ParentId.Value);
                if (parent == null || parent.IsDeleted || parent.GroupId != groupId)
                    throw new ApiException("The comment you are replying to is not in this project any more.", 400);

                // One level deep: answering a reply continues under the comment that reply answers.
                if (parent.ParentId.HasValue)
                {
                    parent = await Comments.GetByIdAsync(parent.ParentId.Value);
                    if (parent == null || parent.IsDeleted)
                        throw new ApiException("The comment you are replying to is not in this project any more.", 400);
                }
            }

            var comment = new GroupComment(text, groupId, me, parent?.Id);
            await Comments.AddAsync(comment);
            await _unitOfWork.CompleteAsync();

            await AnnounceAsync(groupId, "added", comment, string.IsNullOrEmpty(clientId) ? null : clientId);
            await NotifyAsync(group, comment, parent, dto.MentionedUserIds);

            var authors = await AuthorsAsync(new[] { comment });
            var sent = ToDto(comment, authors, me, canManage: true);
            sent.ClientId = string.IsNullOrEmpty(clientId) ? null : clientId;
            return sent;
        }

        public async Task<GroupCommentDto?> UpdateAsync(Guid commentId, string content)
        {
            var comment = await Comments.GetByIdAsync(commentId);
            if (comment == null || comment.IsDeleted) return null;
            if (await FindLiveGroupAsync(comment.GroupId) == null) return null;

            // Someone who is not on the project gets the project's own answer,
            // whoever wrote the comment: leaving a project ends the right to
            // change its discussion.
            await _access.RequireAsync(_access.CanViewGroupAsync(comment.GroupId), NoAccess);

            // Narrower than deleting: the owner may remove a comment, but nobody
            // rewrites what someone else said.
            var me = _currentUser.RequireUserId();
            if (comment.AuthorId != me)
                throw new ApiException("Only the comment's author can edit it.", 403);

            comment.Content = ValidateContent(content);
            comment.SetUpdated(me.ToString());
            await _unitOfWork.CompleteAsync();

            await AnnounceAsync(comment.GroupId, "edited", comment);

            var authors = await AuthorsAsync(new[] { comment });
            return ToDto(comment, authors, me, canManage: true);
        }

        public async Task<bool> DeleteAsync(Guid commentId)
        {
            var comment = await Comments.GetByIdAsync(commentId);
            if (comment == null || comment.IsDeleted) return false;
            if (await FindLiveGroupAsync(comment.GroupId) == null) return false;

            await _access.RequireAsync(_access.CanViewGroupAsync(comment.GroupId), NoAccess);

            var me = _currentUser.RequireUserId();
            if (comment.AuthorId != me && !await _access.CanManageGroupAsync(comment.GroupId))
                throw new ApiException("Only the comment's author or the project owner can delete it.", 403);

            // The replies go with the comment they answer: left behind, they
            // would be answers to nothing.
            var replies = (await Comments.FindAsync(c => c.ParentId == commentId && !c.IsDeleted)).ToList();

            comment.SoftDelete(me.ToString());
            foreach (var reply in replies) reply.SoftDelete(me.ToString());
            await _unitOfWork.CompleteAsync();

            var deletedIds = new List<Guid> { comment.Id };
            deletedIds.AddRange(replies.OrderBy(r => r.CreatedAt).Select(r => r.Id));

            await SafelyAsync(async () =>
            {
                var audience = await _people.GetProjectAudienceAsync(comment.GroupId);
                audience.Add(me);

                await _realtime.NotifyUsersAsync(audience, RealtimeEvent, new GroupCommentEventDto
                {
                    GroupId = comment.GroupId,
                    Action = "deleted",
                    DeletedIds = deletedIds,
                    Total = await CountAsync(comment.GroupId)
                });
            });

            return true;
        }

        // ── Rules ─────────────────────────────────────────────────────────────

        /// <summary>404 for a project that is not there, 403 for a caller who is not on it.</summary>
        private async Task<Group> RequireProjectAsync(Guid groupId)
        {
            var group = await FindLiveGroupAsync(groupId)
                ?? throw new ApiException("Project not found.", 404);

            await _access.RequireAsync(_access.CanViewGroupAsync(groupId), NoAccess);
            return group;
        }

        private async Task<Group?> FindLiveGroupAsync(Guid groupId)
        {
            var group = await _unitOfWork.Repository<Group>().GetByIdAsync(groupId);
            return group == null || group.IsDeleted ? null : group;
        }

        /// <summary>The limits of a task comment: not empty, 2000 characters at most.</summary>
        private static string ValidateContent(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new ApiException("A comment cannot be empty.", 400);

            var text = content.Trim();
            if (text.Length > GroupComment.MaxContentLength)
                throw new ApiException($"Comments are limited to {GroupComment.MaxContentLength} characters.", 400);

            return text;
        }

        private Task<int> CountAsync(Guid groupId) =>
            Comments.CountAsync(c => c.GroupId == groupId && !c.IsDeleted);

        // ── Live event ────────────────────────────────────────────────────────

        /// <summary>
        /// Shows a new or changed comment to everyone on the project, the writer
        /// included (their other tabs). What a person may do with a comment
        /// depends on who they are, so the audience is told in up to three
        /// groups: the author, the others who may delete it (the owner and app
        /// admins), and everyone else.
        /// </summary>
        private Task AnnounceAsync(Guid groupId, string action, GroupComment comment, string? clientId = null) =>
            SafelyAsync(async () =>
            {
                var audience = await _people.GetProjectAudienceAsync(groupId);
                audience.Add(comment.AuthorId);

                var managers = (await _unitOfWork.Repository<Group>()
                    .SelectAsync(g => g.Id == groupId, g => g.OwnerId)).ToHashSet();
                managers.UnionWith(await _unitOfWork.Repository<User>()
                    .SelectAsync(u => audience.Contains(u.Id) && u.Role == UserRole.Admin, u => u.Id));

                var authors = await AuthorsAsync(new[] { comment });
                var total = await CountAsync(groupId);

                var author = new[] { comment.AuthorId };
                var canDelete = audience.Where(id => id != comment.AuthorId && managers.Contains(id)).ToList();
                var readers = audience.Where(id => id != comment.AuthorId && !managers.Contains(id)).ToList();

                async Task SendAsync(IReadOnlyCollection<Guid> to, bool mine, bool manages)
                {
                    if (to.Count == 0) return;

                    var dto = ToDto(comment, authors, mine ? comment.AuthorId : Guid.Empty, manages);
                    dto.ClientId = clientId;

                    await _realtime.NotifyUsersAsync(to, RealtimeEvent, new GroupCommentEventDto
                    {
                        GroupId = groupId,
                        Action = action,
                        Comment = dto,
                        Total = total
                    });
                }

                await SendAsync(author, mine: true, manages: true);
                await SendAsync(canDelete, mine: false, manages: true);
                await SendAsync(readers, mine: false, manages: false);
            });

        // ── Mentions and notifications ────────────────────────────────────────

        /// <summary>
        /// Tells the people who were @mentioned, and the author of the comment
        /// that was answered. Both notifications point at the project and carry
        /// the comment, so opening one lands on it in the discussion.
        /// Non-fatal by design: the comment is already saved.
        /// </summary>
        private Task NotifyAsync(Group group, GroupComment comment, GroupComment? parent, IEnumerable<Guid>? mentionedUserIds) =>
            SafelyAsync(async () =>
            {
                var authorId = comment.AuthorId;

                var requested = (mentionedUserIds ?? Enumerable.Empty<Guid>())
                    .Where(id => id != Guid.Empty && id != authorId)
                    .Distinct()
                    .Take(MaxMentionsPerComment)
                    .ToList();

                // Only people who are on the project. The ids come from the
                // client; without this anyone could be sent a notification by
                // posting their id.
                var onProject = await _people.GetProjectAudienceAsync(group.Id);
                var mentioned = requested.Where(onProject.Contains).ToList();

                if (mentioned.Count > 0)
                {
                    // One unread mention per person and project is enough for the
                    // Mentions list; the notification below is sent every time.
                    var waiting = (await _unitOfWork.Repository<Mention>().SelectAsync(
                            m => m.MentionedByUserId == authorId && mentioned.Contains(m.MentionedUserId)
                                 && m.EntityType == EntityType.Project && m.EntityId == group.Id
                                 && !m.IsRead && !m.IsDeleted,
                            m => m.MentionedUserId))
                        .ToHashSet();

                    foreach (var userId in mentioned.Where(id => !waiting.Contains(id)))
                        await _unitOfWork.Repository<Mention>()
                            .AddAsync(new Mention(authorId, userId, EntityType.Project, group.Id));
                }

                var stagedMentions = await _notifications.StageAsync(
                    new NotificationContent(
                        NotificationType.MentionedInTask, "You were mentioned",
                        actor => $"{actor} mentioned you in \"{group.Name}\".",
                        authorId, group.Id, "Group", comment.Id),
                    mentioned);

                // A reply is news for whoever wrote the comment it answers -
                // unless they were just told because they were mentioned in it,
                // or are not on the project any more.
                var stagedReply = StagedNotifications.None;
                if (parent != null && parent.AuthorId != authorId
                    && !mentioned.Contains(parent.AuthorId) && onProject.Contains(parent.AuthorId))
                {
                    var preview = comment.Content.Length > 80 ? comment.Content[..77] + "…" : comment.Content;

                    stagedReply = await _notifications.StageAsync(
                        new NotificationContent(
                            NotificationType.MentionedInTask, "New reply",
                            actor => $"{actor} replied to your comment in \"{group.Name}\": {preview}",
                            authorId, group.Id, "Group", comment.Id),
                        new[] { parent.AuthorId });
                }

                if (mentioned.Count == 0 && stagedReply.Items.Count == 0) return;

                // Saved together: the mentions and what tells people about them.
                await _unitOfWork.CompleteAsync();
                await _notifications.DeliverAsync(stagedMentions);
                await _notifications.DeliverAsync(stagedReply);
            });

        // ── Mapping ───────────────────────────────────────────────────────────

        private async Task<Dictionary<Guid, User>> AuthorsAsync(IEnumerable<GroupComment> comments)
        {
            var ids = comments.Select(c => c.AuthorId).Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<Guid, User>();

            return (await _unitOfWork.Repository<User>().FindAsync(u => ids.Contains(u.Id)))
                .ToDictionary(u => u.Id);
        }

        private static GroupCommentDto ToDto(
            GroupComment comment, IReadOnlyDictionary<Guid, User> authors, Guid viewerId, bool canManage)
        {
            authors.TryGetValue(comment.AuthorId, out var author);
            var mine = comment.AuthorId == viewerId;

            return new GroupCommentDto
            {
                Id = comment.Id,
                Content = comment.Content,
                GroupId = comment.GroupId,
                ParentId = comment.ParentId,
                AuthorId = comment.AuthorId,
                AuthorName = author?.FullName,
                AuthorProfilePictureUrl = author?.ProfilePictureUrl,
                CreatedAt = comment.CreatedAt,
                UpdatedAt = comment.UpdatedAt,
                IsEdited = comment.UpdatedAt.HasValue,
                CanEdit = mine,
                CanDelete = mine || canManage
            };
        }

        /// <summary>For what happens after the change is saved: failing to tell people must not fail the request.</summary>
        private static async Task SafelyAsync(Func<Task> work)
        {
            try
            {
                await work();
            }
            catch
            {
                // Best-effort — the change itself is already saved.
            }
        }
    }
}
