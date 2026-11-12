using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Miqat.Application.Services
{
    public class CommentService : ICommentService
    {
        private const int MaxCommentLength = 2000;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IAccessPolicy _access;
        private readonly ICurrentUserService _currentUser;
        private readonly IRealtimeNotifier _realtime;
        private readonly INotificationSender _notifications;

        public CommentService(
            IUnitOfWork unitOfWork,
            IAccessPolicy access,
            ICurrentUserService currentUser,
            IRealtimeNotifier realtime,
            INotificationSender notifications)
        {
            _unitOfWork = unitOfWork;
            _access = access;
            _currentUser = currentUser;
            _realtime = realtime;
            _notifications = notifications;
        }

        public async Task<IEnumerable<CommentDto>> GetForTaskAsync(Guid taskId)
        {
            // Reading the thread requires the same visibility as the task itself.
            await _access.RequireAsync(
                _access.CanViewTaskAsync(taskId), "You do not have access to this task.");

            var comments = await _unitOfWork.Repository<Comment>()
                .FindAsync(c => c.TaskId == taskId && !c.IsDeleted);

            var authorIds = comments.Select(c => c.AuthorId).Distinct().ToHashSet();
            var authors = (await _unitOfWork.Repository<User>()
                    .FindAsync(u => authorIds.Contains(u.Id)))
                .ToDictionary(u => u.Id);

            return comments
                .OrderBy(c => c.CreatedAt)
                .Select(c => MapToDto(c, authors.GetValueOrDefault(c.AuthorId)))
                .ToList();
        }

        public async Task<CommentDto> AddAsync(
            Guid taskId, string content, IEnumerable<Guid>? mentionedUserIds = null)
        {
            var authorId = _currentUser.RequireUserId();

            await _access.RequireAsync(
                _access.CanViewTaskAsync(taskId), "You do not have access to this task.");

            var text = ValidateContent(content);

            var comment = new Comment(text, taskId, authorId);
            await _unitOfWork.Repository<Comment>().AddAsync(comment);
            await _unitOfWork.CompleteAsync();

            await NotifyParticipantsAsync(taskId, authorId, comment);
            await HandleMentionsAsync(taskId, authorId, mentionedUserIds, comment.Id);

            var author = await _unitOfWork.Repository<User>().GetByIdAsync(authorId);
            return MapToDto(comment, author);
        }

        public async Task<CommentDto?> UpdateAsync(Guid commentId, string content)
        {
            var comment = await _unitOfWork.Repository<Comment>().GetByIdAsync(commentId);
            if (comment == null || comment.IsDeleted) return null;

            // Editing is narrower than deleting: an admin may remove a comment,
            // but nobody rewrites what someone else said.
            var me = _currentUser.RequireUserId();
            if (comment.AuthorId != me)
                throw new ApiException("Only the comment's author can edit it.", 403);

            // Losing access to the task ends the right to change its discussion.
            await _access.RequireAsync(
                _access.CanViewTaskAsync(comment.TaskId), "You do not have access to this task.");

            comment.Content = ValidateContent(content);
            comment.SetUpdated(me.ToString());
            await _unitOfWork.CompleteAsync();

            await RefreshThreadAsync(comment.TaskId, me);

            var author = await _unitOfWork.Repository<User>().GetByIdAsync(me);
            return MapToDto(comment, author);
        }

        public async Task<bool> DeleteAsync(Guid commentId)
        {
            var comment = await _unitOfWork.Repository<Comment>().GetByIdAsync(commentId);
            if (comment == null || comment.IsDeleted) return false;

            // Only the author may retract what they wrote; the task's visibility
            // rules do not extend to erasing someone else's words.
            var me = _currentUser.RequireUserId();
            if (!_currentUser.IsAdmin && comment.AuthorId != me)
                throw new ApiException("Only the comment's author can delete it.", 403);

            comment.SoftDelete(me.ToString());
            await _unitOfWork.CompleteAsync();

            await RefreshThreadAsync(comment.TaskId, me);
            return true;
        }

        private static string ValidateContent(string? content)
        {
            if (string.IsNullOrWhiteSpace(content))
                throw new ApiException("A comment cannot be empty.", 400);

            var text = content.Trim();
            if (text.Length > MaxCommentLength)
                throw new ApiException($"Comments are limited to {MaxCommentLength} characters.", 400);

            return text;
        }

        /// <summary>
        /// Tells anyone who may have the thread open (the task's creator and
        /// assignee, and the actor's other tabs) to reload it. The client reloads
        /// the whole thread on "commentAdded", so the same event serves edits and
        /// deletions. Best-effort.
        /// </summary>
        private async Task RefreshThreadAsync(Guid taskId, Guid actorId)
        {
            try
            {
                var task = await _unitOfWork.Repository<TaskItem>().GetByIdAsync(taskId);
                if (task == null) return;

                var audience = new HashSet<Guid> { actorId, task.UserId };
                if (task.AssignedToUserId.HasValue) audience.Add(task.AssignedToUserId.Value);

                await _realtime.NotifyUsersAsync(audience, "commentAdded", new { taskId });
            }
            catch
            {
                // Best-effort — the change itself is already saved.
            }
        }

        /// <summary>
        /// Tells the task's creator and assignee about the new comment — except
        /// the commenter themselves. Non-fatal by design: the comment is already
        /// saved, so a notification hiccup must not fail the request.
        /// </summary>
        private async Task NotifyParticipantsAsync(Guid taskId, Guid authorId, Comment comment)
        {
            try
            {
                var task = await _unitOfWork.Repository<TaskItem>().GetByIdAsync(taskId);
                if (task == null) return;

                var recipients = new HashSet<Guid>();
                if (task.UserId != authorId) recipients.Add(task.UserId);
                if (task.AssignedToUserId.HasValue && task.AssignedToUserId.Value != authorId)
                    recipients.Add(task.AssignedToUserId.Value);

                var preview = comment.Content.Length > 80
                    ? comment.Content[..77] + "…"
                    : comment.Content;

                await _notifications.SendAsync(
                    new NotificationContent(
                        Domain.Enumerations.NotificationType.MentionedInTask, "New comment",
                        actor => $"{actor} commented on \"{task.Title}\": {preview}",
                        authorId, task.Id, "TaskItem", comment.Id),
                    recipients);

                // Anyone with the thread open — participants and the author's
                // other tabs — reloads it live.
                var threadAudience = new HashSet<Guid>(recipients) { authorId };
                await _realtime.NotifyUsersAsync(threadAudience, "commentAdded",
                    new { taskId = task.Id });
            }
            catch
            {
                // Swallowed on purpose — see the summary above.
            }
        }

        /// <summary>
        /// Everyone who can be @mentioned here: the task's creator and assignee,
        /// the members and owner of its project, and the caller's own friends —
        /// the last group being the point of "pull someone in for help".
        /// </summary>
        public async Task<IEnumerable<MentionableUserDto>> GetMentionableAsync(Guid taskId)
        {
            await _access.RequireAsync(
                _access.CanViewTaskAsync(taskId), "You do not have access to this task.");

            var me = _currentUser.RequireUserId();
            var task = await _unitOfWork.Repository<TaskItem>().GetByIdAsync(taskId);
            if (task == null) return Enumerable.Empty<MentionableUserDto>();

            // Relationship wins by specificity, so a friend who is also on the
            // project reads as "Project member" rather than "Friend".
            var relationships = new Dictionary<Guid, string>();

            void Note(Guid id, string relationship)
            {
                if (id != me && !relationships.ContainsKey(id)) relationships[id] = relationship;
            }

            Note(task.UserId, "Task owner");
            if (task.AssignedToUserId.HasValue) Note(task.AssignedToUserId.Value, "Assignee");

            var withAccess = new HashSet<Guid>(relationships.Keys);

            if (task.GroupId.HasValue)
            {
                var groupId = task.GroupId.Value;
                var group = await _unitOfWork.Repository<Group>().GetByIdAsync(groupId);
                if (group != null) { Note(group.OwnerId, "Project owner"); withAccess.Add(group.OwnerId); }

                var members = await _unitOfWork.Repository<GroupMember>()
                    .FindAsync(gm => gm.GroupId == groupId);
                foreach (var member in members) { Note(member.UserId, "Project member"); withAccess.Add(member.UserId); }
            }

            // Friends, so you can bring in someone who cannot see this yet.
            var friendships = await _unitOfWork.Repository<Friendship>()
                .FindAsync(f => (f.SenderId == me || f.ReceiverId == me)
                                && f.Status == Domain.Enumerations.FriendshipStatus.Accepted
                                && !f.IsDeleted);
            foreach (var friendship in friendships)
                Note(friendship.SenderId == me ? friendship.ReceiverId : friendship.SenderId, "Friend");

            var ids = relationships.Keys.ToHashSet();
            var users = await _unitOfWork.Repository<User>()
                .FindAsync(u => ids.Contains(u.Id) && !u.IsDeleted && u.IsActive);

            return users
                .Select(user => new MentionableUserDto
                {
                    UserId = user.Id,
                    FullName = user.FullName,
                    Email = user.Email,
                    ProfilePictureUrl = user.ProfilePictureUrl,
                    Relationship = relationships[user.Id],
                    HasAccess = withAccess.Contains(user.Id)
                })
                .OrderBy(u => u.Relationship == "Friend" ? 1 : 0)
                .ThenBy(u => u.FullName)
                .ToList();
        }

        /// <summary>
        /// Records the mention, tells the person, and — when the mentioner is
        /// entitled to do so — adds them to the task's project so the mention is
        /// actually useful. Mentioning someone who cannot open the task would
        /// otherwise be an invitation to a locked door.
        /// </summary>
        private async Task HandleMentionsAsync(
            Guid taskId, Guid authorId, IEnumerable<Guid>? mentionedUserIds, Guid commentId)
        {
            var requested = (mentionedUserIds ?? Enumerable.Empty<Guid>())
                .Where(id => id != Guid.Empty && id != authorId)
                .Distinct()
                .ToList();
            if (requested.Count == 0) return;

            try
            {
                // Only people who can actually be mentioned here. The ids come
                // from the client, so without this check anyone could be sent a
                // notification - or, through the access grant below, be added to
                // a project - by posting their id.
                var allowed = (await GetMentionableAsync(taskId)).Select(u => u.UserId).ToHashSet();
                var mentioned = requested.Where(allowed.Contains).ToList();
                if (mentioned.Count == 0) return;

                var task = await _unitOfWork.Repository<TaskItem>().GetByIdAsync(taskId);
                if (task == null) return;

                // Only someone who owns the task or the project may widen access.
                var canGrantAccess = task.UserId == authorId
                    || (task.GroupId.HasValue && await _access.CanManageGroupAsync(task.GroupId.Value));

                foreach (var userId in mentioned)
                {
                    var alreadyMentioned = await _unitOfWork.Repository<Mention>()
                        .FindAsync(m => m.MentionedByUserId == authorId
                                        && m.MentionedUserId == userId
                                        && m.EntityId == taskId
                                        && !m.IsDeleted);

                    if (!alreadyMentioned.Any())
                    {
                        await _unitOfWork.Repository<Mention>().AddAsync(new Mention(
                            authorId, userId, Domain.Enumerations.EntityType.Task, taskId));
                    }

                    if (canGrantAccess && task.GroupId.HasValue)
                    {
                        var groupId = task.GroupId.Value;
                        var existing = await _unitOfWork.Repository<GroupMember>()
                            .FindAsync(gm => gm.GroupId == groupId && gm.UserId == userId);
                        var group = await _unitOfWork.Repository<Group>().GetByIdAsync(groupId);

                        if (!existing.Any() && group?.OwnerId != userId)
                        {
                            await _unitOfWork.Repository<GroupMember>()
                                .AddAsync(new GroupMember(groupId, userId));
                        }
                    }
                }

                // Saved together with the mentions and any access that was granted.
                var staged = await _notifications.StageAsync(
                    new NotificationContent(
                        Domain.Enumerations.NotificationType.MentionedInTask, "You were mentioned",
                        actor => $"{actor} mentioned you in \"{task.Title}\".",
                        authorId, taskId, "TaskItem", commentId),
                    mentioned);

                await _unitOfWork.CompleteAsync();
                await _notifications.DeliverAsync(staged);
            }
            catch
            {
                // Non-fatal: the comment itself is already saved.
            }
        }

        private static CommentDto MapToDto(Comment comment, User? author) => new()
        {
            Id = comment.Id,
            Content = comment.Content,
            TaskId = comment.TaskId,
            AuthorId = comment.AuthorId,
            AuthorName = author?.FullName,
            AuthorProfilePictureUrl = author?.ProfilePictureUrl,
            CreatedAt = comment.CreatedAt,
            UpdatedAt = comment.UpdatedAt,
            IsEdited = comment.UpdatedAt.HasValue
        };
    }
}
