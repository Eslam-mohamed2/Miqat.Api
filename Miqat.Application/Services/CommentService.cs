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
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAccessPolicy _access;
        private readonly ICurrentUserService _currentUser;
        private readonly IRealtimeNotifier _realtime;

        public CommentService(
            IUnitOfWork unitOfWork,
            IAccessPolicy access,
            ICurrentUserService currentUser,
            IRealtimeNotifier realtime)
        {
            _unitOfWork = unitOfWork;
            _access = access;
            _currentUser = currentUser;
            _realtime = realtime;
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
                .Select(c => MapToDto(c, authors.GetValueOrDefault(c.AuthorId)));
        }

        public async Task<CommentDto> AddAsync(
            Guid taskId, string content, IEnumerable<Guid>? mentionedUserIds = null)
        {
            var authorId = _currentUser.RequireUserId();

            await _access.RequireAsync(
                _access.CanViewTaskAsync(taskId), "You do not have access to this task.");

            if (string.IsNullOrWhiteSpace(content))
                throw new ApiException("A comment cannot be empty.", 400);
            if (content.Length > 2000)
                throw new ApiException("Comments are limited to 2000 characters.", 400);

            var comment = new Comment(content.Trim(), taskId, authorId);
            await _unitOfWork.Repository<Comment>().AddAsync(comment);
            await _unitOfWork.CompleteAsync();

            await NotifyParticipantsAsync(taskId, authorId, comment);
            await HandleMentionsAsync(taskId, authorId, mentionedUserIds, comment.Id);

            var author = await _unitOfWork.Repository<User>().GetByIdAsync(authorId);
            return MapToDto(comment, author);
        }

        public async Task<bool> DeleteAsync(Guid commentId)
        {
            var comment = await _unitOfWork.Repository<Comment>().GetByIdAsync(commentId);
            if (comment == null || comment.IsDeleted) return false;

            // Only the author may retract what they wrote; the task's visibility
            // rules do not extend to erasing someone else's words.
            if (!_currentUser.IsAdmin && comment.AuthorId != _currentUser.RequireUserId())
                throw new ApiException("Only the comment's author can delete it.", 403);

            comment.SoftDelete();
            _unitOfWork.Repository<Comment>().Update(comment);
            return await _unitOfWork.CompleteAsync() > 0;
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

                var author = await _unitOfWork.Repository<User>().GetByIdAsync(authorId);
                var actorName = string.IsNullOrWhiteSpace(author?.FullName) ? "Someone" : author!.FullName;

                var recipients = new HashSet<Guid>();
                if (task.UserId != authorId) recipients.Add(task.UserId);
                if (task.AssignedToUserId.HasValue && task.AssignedToUserId.Value != authorId)
                    recipients.Add(task.AssignedToUserId.Value);

                var preview = comment.Content.Length > 80
                    ? comment.Content[..77] + "…"
                    : comment.Content;

                foreach (var recipientId in recipients)
                {
                    await _unitOfWork.Repository<Notification>().AddAsync(new Notification(
                        title: "New comment",
                        message: $"{actorName} commented on \"{task.Title}\": {preview}",
                        type: Domain.Enumerations.NotificationType.MentionedInTask,
                        recipientUserId: recipientId,
                        triggeredByUserId: authorId,
                        linkedEntityId: task.Id,
                        linkedEntityType: "TaskItem",
                        linkedCommentId: comment.Id));
                }

                if (recipients.Count > 0) await _unitOfWork.CompleteAsync();

                await _realtime.NotifyUsersAsync(recipients, "notification", new
                {
                    title = "New comment",
                    message = $"{actorName} commented on \"{task.Title}\"",
                    type = "MentionedInTask",
                    linkedEntityId = task.Id,
                    linkedEntityType = "TaskItem",
                    linkedCommentId = comment.Id,
                    triggeredByUserName = actorName
                });

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
            var users = await _unitOfWork.Repository<User>().FindAsync(u => ids.Contains(u.Id));

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
                .ThenBy(u => u.FullName);
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
            var mentioned = (mentionedUserIds ?? Enumerable.Empty<Guid>())
                .Where(id => id != Guid.Empty && id != authorId)
                .Distinct()
                .ToList();
            if (mentioned.Count == 0) return;

            try
            {
                var task = await _unitOfWork.Repository<TaskItem>().GetByIdAsync(taskId);
                if (task == null) return;

                var author = await _unitOfWork.Repository<User>().GetByIdAsync(authorId);
                var actorName = string.IsNullOrWhiteSpace(author?.FullName) ? "Someone" : author!.FullName;

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

                    await _unitOfWork.Repository<Notification>().AddAsync(new Notification(
                        title: "You were mentioned",
                        message: $"{actorName} mentioned you in \"{task.Title}\".",
                        type: Domain.Enumerations.NotificationType.MentionedInTask,
                        recipientUserId: userId,
                        triggeredByUserId: authorId,
                        linkedEntityId: taskId,
                        linkedEntityType: "TaskItem",
                        linkedCommentId: commentId));
                }

                await _unitOfWork.CompleteAsync();

                await _realtime.NotifyUsersAsync(mentioned, "notification", new
                {
                    title = "You were mentioned",
                    message = $"{actorName} mentioned you in \"{task.Title}\"",
                    type = "MentionedInTask",
                    linkedEntityId = taskId,
                    linkedEntityType = "TaskItem",
                    linkedCommentId = commentId,
                    triggeredByUserName = actorName
                });
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
            UpdatedAt = comment.UpdatedAt
        };
    }
}
