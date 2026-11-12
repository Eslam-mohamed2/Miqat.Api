using Miqat.Application.Modules;

namespace Miqat.Application.Interfaces
{
    public interface ISearchService
    {
        /// <summary>
        /// Searches everything the caller can already see: their tasks (owned,
        /// assigned, or inside their projects), their projects, and other people.
        /// </summary>
        Task<SearchResultsDto> SearchAsync(string query, int limitPerSection);
    }
}
