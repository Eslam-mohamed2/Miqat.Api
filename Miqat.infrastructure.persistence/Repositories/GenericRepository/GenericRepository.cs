using Microsoft.EntityFrameworkCore;
using Miqat.Application.Interfaces;
using Miqat.Domain.Specifications;
using Miqat.infrastructure.persistence.Data;
using Miqat.infrastructure.persistence.Specifications;
using System.Linq.Expressions;


namespace Miqat.infrastructure.persistence.Repositories.GenericRepository
{
    public class GenericRepository<T> : IGenericRepository<T> where T : class
    {
        private readonly MiqatDbContext _context;

        public GenericRepository(MiqatDbContext context) => _context = context;

        public async Task<IReadOnlyList<T>> GetAllAsync()
            => await _context.Set<T>().ToListAsync();

        public async Task<T?> GetByIdAsync(Guid id)
            => await _context.Set<T>().FindAsync(id);

        public async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate)
            => await _context.Set<T>().Where(predicate).ToListAsync();

        /// <summary>
        /// Counts matching rows per key in a single grouped query.
        /// </summary>
        /// <remarks>
        /// Exists so list endpoints can attach counts without either loading the
        /// child collections or issuing one COUNT per parent. Both alternatives
        /// scale with the number of parents; this does not.
        /// </remarks>
        public async Task<Dictionary<Guid, int>> CountGroupedAsync(
            Expression<Func<T, bool>> predicate, Expression<Func<T, Guid>> keySelector)
            => await _context.Set<T>()
                .Where(predicate)
                .GroupBy(keySelector)
                .Select(g => new { Key = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);

        public async Task<bool> AnyAsync(Expression<Func<T, bool>> predicate)
            => await _context.Set<T>().AnyAsync(predicate);

        public async Task<int> CountAsync(Expression<Func<T, bool>> predicate)
            => await _context.Set<T>().CountAsync(predicate);

        public async Task<List<TResult>> SelectAsync<TResult>(
            Expression<Func<T, bool>> predicate, Expression<Func<T, TResult>> selector)
            => await _context.Set<T>().AsNoTracking().Where(predicate).Select(selector).ToListAsync();

        public async Task AddAsync(T entity)
            => await _context.Set<T>().AddAsync(entity);

        public void Update(T entity)
            => _context.Set<T>().Update(entity);

        public void Delete(T entity)
            => _context.Set<T>().Remove(entity);

        public void DeleteRange(IEnumerable<T> entities)
            => _context.Set<T>().RemoveRange(entities);

        // Specification methods
        public async Task<T?> GetEntityWithSpec(ISpecification<T> spec)
            => await ApplySpecification(spec).FirstOrDefaultAsync();

        // Lists are read-only snapshots (see IGenericRepository.ListAsync):
        // skipping change tracking roughly halves the cost of a large list.
        public async Task<IReadOnlyList<T>> ListAsync(ISpecification<T> spec)
            => await ApplySpecification(spec).AsNoTracking().ToListAsync();

        public async Task<int> CountAsync(ISpecification<T> spec)
            => await ApplySpecification(spec).CountAsync();

        private IQueryable<T> ApplySpecification(ISpecification<T> spec)
            => SpecificationEvaluator<T>.GetQuery(
                _context.Set<T>().AsQueryable(), spec);
    }
}
