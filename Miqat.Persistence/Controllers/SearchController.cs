using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Miqat.Application.Interfaces;

namespace Miqat.API.Controllers
{
    /// <summary>
    /// Global search: one call that looks through the caller's tasks, projects
    /// and other people. Returns { query, tasks, projects, users }.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class SearchController : ControllerBase
    {
        private readonly ISearchService _search;

        public SearchController(ISearchService search) => _search = search;

        /// <param name="query">What to look for (2 to 100 characters).</param>
        /// <param name="limit">Maximum results per section (1 to 25, default 10).</param>
        [HttpGet]
        public async Task<IActionResult> Search([FromQuery] string? query, [FromQuery] int limit = 10)
        {
            var term = query?.Trim() ?? string.Empty;

            if (term.Length < 2)
                return BadRequest(new { message = "Type at least 2 characters to search." });

            if (term.Length > 100)
                return BadRequest(new { message = "Search query cannot exceed 100 characters." });

            return Ok(await _search.SearchAsync(term, limit));
        }
    }
}
