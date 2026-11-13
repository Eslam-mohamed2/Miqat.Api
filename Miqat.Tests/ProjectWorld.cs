using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Services;
using Miqat.Domain.Common;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;
using Miqat.Domain.Specifications;
using System.Linq.Expressions;

namespace Miqat.Tests;

/// <summary>
/// A few people, their projects, and the discussion service over in-memory
/// rows. What the service pushes live and who it notifies is recorded, so a
/// test can say what everyone was told.
/// </summary>
internal sealed class ProjectWorld
{
    public List<User> Users { get; } = new();
    public List<Group> Groups { get; } = new();
    public List<GroupMember> Members { get; } = new();
    public List<GroupComment> Comments { get; } = new();
    public List<Mention> Mentions { get; } = new();

    public RecordingRealtime Realtime { get; } = new();
    public RecordingNotifications Notifications { get; } = new();

    private readonly FakeUnitOfWork _uow = new();

    public ProjectWorld()
    {
        _uow.Register<User>(new FakeRepository<User>(Users, u => u.Id));
        _uow.Register<Group>(new FakeRepository<Group>(Groups, g => g.Id));
        _uow.Register<GroupMember>(new FakeRepository<GroupMember>(Members, m => m.Id));
        _uow.Register<GroupComment>(new SpecRepository<GroupComment>(Comments, c => c.Id));
        _uow.Register<Mention>(new FakeRepository<Mention>(Mentions, m => m.Id));
    }

    public User Person(string name, UserRole role = UserRole.User)
    {
        var user = new User(name, $"{name.ToLowerInvariant().Replace(' ', '.')}@example.com", DateTime.UtcNow.AddYears(-30), null, null)
        {
            Role = role
        };
        Users.Add(user);
        return user;
    }

    /// <summary>A project of this owner with these members. Like one made in the app, the owner has no member row.</summary>
    public Group Project(string name, User owner, params User[] members)
    {
        var group = new Group(name, null, owner.Id);
        Groups.Add(group);
        foreach (var member in members) Members.Add(new GroupMember(group.Id, member.Id));
        return group;
    }

    public void Remove(Group group, User member) =>
        Members.RemoveAll(m => m.GroupId == group.Id && m.UserId == member.Id);

    /// <summary>The discussion service as this person calls it.</summary>
    public GroupCommentService As(User user)
    {
        var caller = new FakeCurrentUser(user.Id, user.Role.ToString());
        return new GroupCommentService(
            _uow, new AccessPolicy(_uow, caller), caller, Realtime, Notifications, new PeopleDirectory(_uow));
    }

    /// <summary>The projects service as this person calls it.</summary>
    public GroupService ProjectsAs(User user)
    {
        var caller = new FakeCurrentUser(user.Id, user.Role.ToString());
        return new GroupService(_uow, new GroupMapper(), new AccessPolicy(_uow, caller), caller, Notifications);
    }

    /// <summary>Writes a comment (or a reply) and moves it back in time, so the order of a thread is certain.</summary>
    public async Task<GroupCommentDto> SayAsync(User author, Group group, string text, Guid? parentId = null, int minutesAgo = 0)
    {
        var dto = await As(author).AddAsync(group.Id, new CreateGroupCommentDto { Content = text, ParentId = parentId });
        if (minutesAgo > 0) Backdate(dto.Id, minutesAgo);
        return dto;
    }

    public void Backdate(Guid commentId, int minutesAgo) =>
        typeof(BaseEntity).GetProperty(nameof(BaseEntity.CreatedAt))!
            .SetValue(Row(commentId), DateTime.UtcNow.AddMinutes(-minutesAgo));

    public GroupComment Row(Guid commentId) => Comments.Single(c => c.Id == commentId);

    /// <summary>The live events about a project's discussion: who heard which comment, and what they may do with it.</summary>
    public List<(HashSet<Guid> To, GroupCommentEventDto Event)> Events() =>
        Realtime.Sent
            .Where(s => s.Event == GroupCommentService.RealtimeEvent)
            .Select(s => (s.To, (GroupCommentEventDto)s.Payload))
            .ToList();
}

/// <summary>Records what would have been sent instead of sending it. As the real sender, it never tells the person who caused it.</summary>
internal sealed class RecordingNotifications : INotificationSender
{
    public List<Notification> Sent { get; } = new();

    public Task<StagedNotifications> StageAsync(NotificationContent content, IEnumerable<Guid> recipientIds)
    {
        var message = content.Message("Someone");
        var items = recipientIds.Where(id => id != content.ActorId).Distinct()
            .Select(id => new Notification(
                content.Title, message, content.Type, id, content.ActorId,
                content.LinkedEntityId, content.LinkedEntityType, content.LinkedCommentId))
            .ToList();
        Sent.AddRange(items);

        return Task.FromResult(new StagedNotifications(items, null));
    }

    public Task DeliverAsync(StagedNotifications staged) => Task.CompletedTask;
    public Task SendAsync(NotificationContent content, IEnumerable<Guid> recipientIds) => StageAsync(content, recipientIds);

    public List<Notification> For(User user) => Sent.Where(n => n.RecipientUserId == user.Id).ToList();
}

/// <summary>
/// An in-memory repository that also answers a specification the way the
/// database does: its filter, its order and its page. (The plain fake hands
/// back every row, which is no use for testing pages.)
/// </summary>
internal sealed class SpecRepository<T> : IGenericRepository<T> where T : class
{
    private readonly List<T> _items;
    private readonly FakeRepository<T> _plain;

    public SpecRepository(List<T> items, Func<T, Guid> idOf)
    {
        _items = items;
        _plain = new FakeRepository<T>(items, idOf);
    }

    private IEnumerable<T> Apply(ISpecification<T> spec, bool paged = true)
    {
        IEnumerable<T> rows = _items;
        if (spec.Criteria != null) rows = rows.Where(spec.Criteria.Compile());
        if (spec.OrderBy != null) rows = rows.OrderBy(spec.OrderBy.Compile());
        if (spec.OrderByDescending != null) rows = rows.OrderByDescending(spec.OrderByDescending.Compile());
        if (paged && spec.IsPagingEnabled) rows = rows.Skip(spec.Skip).Take(spec.Take);
        return rows.ToList();
    }

    public Task<T?> GetEntityWithSpec(ISpecification<T> spec) => Task.FromResult(Apply(spec).FirstOrDefault());
    public Task<IReadOnlyList<T>> ListAsync(ISpecification<T> spec) => Task.FromResult<IReadOnlyList<T>>(Apply(spec).ToList());
    public Task<int> CountAsync(ISpecification<T> spec) => Task.FromResult(Apply(spec, paged: false).Count());

    public Task<IReadOnlyList<T>> GetAllAsync() => _plain.GetAllAsync();
    public Task<T?> GetByIdAsync(Guid id) => _plain.GetByIdAsync(id);
    public Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate) => _plain.FindAsync(predicate);
    public Task<Dictionary<Guid, int>> CountGroupedAsync(Expression<Func<T, bool>> predicate, Expression<Func<T, Guid>> keySelector) =>
        _plain.CountGroupedAsync(predicate, keySelector);
    public Task<bool> AnyAsync(Expression<Func<T, bool>> predicate) => _plain.AnyAsync(predicate);
    public Task<int> CountAsync(Expression<Func<T, bool>> predicate) => _plain.CountAsync(predicate);
    public Task<List<TResult>> SelectAsync<TResult>(Expression<Func<T, bool>> predicate, Expression<Func<T, TResult>> selector) =>
        _plain.SelectAsync(predicate, selector);
    public Task AddAsync(T entity) => _plain.AddAsync(entity);
    public void Update(T entity) => _plain.Update(entity);
    public void Delete(T entity) => _plain.Delete(entity);
    public void DeleteRange(IEnumerable<T> entities) => _plain.DeleteRange(entities);
}
