using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using System.Security.Claims;

namespace Miqat.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class GroupController : ControllerBase
    {
        private readonly IGroupService _groupService;

        public GroupController(IGroupService groupService)
        {
            _groupService = groupService;
        }

        private Guid GetCurrentUserId() =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var groups = await _groupService.GetAllGroups();
            return Ok(groups);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetById(Guid id)
        {
            var group = await _groupService.GetGroupById(id);
            if (group == null) return NotFound(new { message = "Group not found." });
            return Ok(group);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] GroupDto dto)
        {
            dto.OwnerId = GetCurrentUserId();
            var created = await _groupService.CreateAsync(dto);
            return CreatedAtAction(nameof(GetById),
                new { id = created.Id }, created);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(Guid id, [FromBody] GroupDto dto)
        {
            var result = await _groupService.UpdateAsync(id, dto);
            if (!result) return NotFound(new { message = "Group not found." });
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var result = await _groupService.DeleteAsync(id);
            if (!result) return NotFound(new { message = "Group not found." });
            return NoContent();
        }

        [HttpPost("{groupId}/members/{userId}")]
        public async Task<IActionResult> AddMember(Guid groupId, Guid userId)
        {
            var result = await _groupService.AddMemberAsync(groupId, userId);
            if (!result) return BadRequest(new { message = "User is already a member." });
            return Ok(new { message = "Member added successfully." });
        }

        [HttpDelete("{groupId}/members/{userId}")]
        public async Task<IActionResult> RemoveMember(Guid groupId, Guid userId)
        {
            var result = await _groupService.RemoveMemberAsync(groupId, userId);
            if (!result) return NotFound(new { message = "Member not found." });
            return Ok(new { message = "Member removed successfully." });
        }
    }
}
