using Miqat.Application.Interfaces;
using Miqat.Domain.Specifications;
using System.Collections.Concurrent;
using System.Linq.Expressions;

namespace Miqat.Tests;

/// <summary>
/// A small in-memory stand-in for the data layer.
///
/// Hand-rolled rather than mocked: the access rules are simple predicates over a
/// handful of rows, so a real list makes each test read like the scenario it
/// describes instead of a pile of setup calls.
/// </summary>
public sealed class FakeRepository<T> : IGenericRepository<T> where T : class
{
    private readonly List<T> _items;
    private readonly Func<T, Guid> _idOf;

    public FakeRepository(List<T> items, Func<T, Guid> idOf)
    {
        _items = items;
        _idOf = idOf;
    }

    public Task<IReadOnlyList<T>> GetAllAsync() => Task.FromResult<IReadOnlyList<T>>(_items);

    public Task<T?> GetByIdAsync(Guid id) =>
        Task.FromResult(_items.FirstOrDefault(x => _idOf(x) == id));

    public Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate) =>
        Task.FromResult(_items.Where(predicate.Compile()));

    public Task<Dictionary<Guid, int>> CountGroupedAsync(
        Expression<Func<T, bool>> predicate, Expression<Func<T, Guid>> keySelector) =>
        Task.FromResult(_items
            .Where(predicate.Compile())
            .GroupBy(keySelector.Compile())
            .ToDictionary(g => g.Key, g => g.Count()));

    public Task AddAsync(T entity) { _items.Add(entity); return Task.CompletedTask; }
    public void Update(T entity) { }
    public void Delete(T entity) { _items.Remove(entity); }

    public Task<T?> GetEntityWithSpec(ISpecification<T> spec) =>
        Task.FromResult(_items.FirstOrDefault());

    public Task<IReadOnlyList<T>> ListAsync(ISpecification<T> spec) =>
        Task.FromResult<IReadOnlyList<T>>(_items);

    public Task<int> CountAsync(ISpecification<T> spec) => Task.FromResult(_items.Count);
}

public sealed class FakeUnitOfWork : IUnitOfWork
{
    private readonly ConcurrentDictionary<Type, object> _repositories = new();

    public void Register<T>(IGenericRepository<T> repository) where T : class =>
        _repositories[typeof(T)] = repository;

    public IGenericRepository<TEntity> Repository<TEntity>() where TEntity : class =>
        _repositories.TryGetValue(typeof(TEntity), out var repo)
            ? (IGenericRepository<TEntity>)repo
            : new FakeRepository<TEntity>(new List<TEntity>(), _ => Guid.Empty);

    public Task<int> CompleteAsync() => Task.FromResult(1);
    public void Dispose() { }
}

public sealed class FakeCurrentUser : ICurrentUserService
{
    public FakeCurrentUser(Guid? userId, string role = "User")
    {
        UserId = userId;
        Role = role;
    }

    public Guid? UserId { get; }
    public string? Role { get; }
    public bool IsAuthenticated => UserId.HasValue;
    public bool IsAdmin => string.Equals(Role, "Admin", StringComparison.OrdinalIgnoreCase);

    public Guid RequireUserId() =>
        UserId ?? throw new Miqat.Application.Common.ApiException("Not signed in.", 401);
}
