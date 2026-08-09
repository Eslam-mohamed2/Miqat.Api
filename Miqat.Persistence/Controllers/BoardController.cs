using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;

namespace Miqat.Persistence.Controllers
{
    /// <summary>Saved whiteboards and node diagrams, scoped to the caller.</summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class BoardController : ControllerBase
    {
        private readonly IBoardService _boards;

        public BoardController(IBoardService boards) => _boards = boards;

        [HttpGet]
        public async Task<IActionResult> GetMine([FromQuery] string? kind)
            => Ok(await _boards.GetMineAsync(kind));

        /// <summary>Backs the bare /whiteboard and /node-flow routes.</summary>
        [HttpGet("latest")]
        public async Task<IActionResult> GetLatest([FromQuery] string kind = "Whiteboard")
        {
            var board = await _boards.GetLatestAsync(kind);
            // 204 rather than 404: "you have no board yet" is a normal first run,
            // not an error the client should surface.
            return board == null ? NoContent() : Ok(board);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var board = await _boards.GetByIdAsync(id);
            return board == null ? NotFound(new { message = "Board not found." }) : Ok(board);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] SaveBoardDto dto)
        {
            var created = await _boards.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] SaveBoardDto dto)
        {
            var updated = await _boards.UpdateAsync(id, dto);
            return updated == null ? NotFound(new { message = "Board not found." }) : Ok(updated);
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
            => await _boards.DeleteAsync(id) ? NoContent() : NotFound(new { message = "Board not found." });
    }
}
