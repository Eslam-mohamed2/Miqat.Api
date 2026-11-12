using Miqat.Domain.Specifications;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.Application.Interfaces
{
    public interface IGenericRepository<T> where T : class
    {
        // Basic
        Task<IReadOnlyList<T>> GetAllAsync();
        Task<T?> GetByIdAsync(Guid id);
        Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate);

        /// <summary>Counts matching rows per key in one grouped query.</summary>
        Task<Dictionary<Guid, int>> CountGroupedAsync(
            Expression<Func<T, bool>> predicate, Expression<Func<T, Guid>> keySelector);

        /// <summary>True when at least one row matches. The rows themselves are not loaded.</summary>
        Task<bool> AnyAsync(Expression<Func<T, bool>> predicate);

        /// <summary>Number of matching rows, counted in the database.</summary>
        Task<int> CountAsync(Expression<Func<T, bool>> predicate);

        /// <summary>
        /// Reads only the selected values of the matching rows, in one query.
        /// Nothing is tracked, so this is for reading, never for changing.
        /// </summary>
        Task<List<TResult>> SelectAsync<TResult>(
            Expression<Func<T, bool>> predicate, Expression<Func<T, TResult>> selector);

        Task AddAsync(T entity);
        void Update(T entity);
        void Delete(T entity);
        void DeleteRange(IEnumerable<T> entities);

        // Specification
        Task<T?> GetEntityWithSpec(ISpecification<T> spec);

        /// <summary>
        /// A read-only list: the rows are not tracked, which is faster and uses
        /// less memory. To change a row, load it with GetByIdAsync, FindAsync
        /// or GetEntityWithSpec instead.
        /// </summary>
        Task<IReadOnlyList<T>> ListAsync(ISpecification<T> spec);
        Task<int> CountAsync(ISpecification<T> spec);

    }
}
