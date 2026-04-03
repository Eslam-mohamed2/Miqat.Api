using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Miqat.Application.Interfaces;
using System.Security.Claims;

namespace Miqat.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class NotificationController : ControllerBase
    {
        private readonly INotificationService _notificationService;

        public NotificationController(INotificationService notificationService)
        {
            _notificationService = notificationService;
        }

        private Guid GetCurrentUserId() =>
            Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var notifications = await _notificationService
                .GetAllNotifications(GetCurrentUserId());
            return Ok(notifications);
        }

        [HttpGet("unread")]
        public async Task<IActionResult> GetUnread()
        {
            var notifications = await _notificationService
                .GetUnreadNotifications(GetCurrentUserId());
            return Ok(notifications);
        }

        [HttpPut("{id}/read")]
        public async Task<IActionResult> MarkAsRead(Guid id)
        {
            var result = await _notificationService.MarkAsReadAsync(id);
            if (!result) return NotFound(new { message = "Notification not found." });
            return Ok(new { message = "Marked as read." });
        }

        [HttpPut("read-all")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            await _notificationService.MarkAllAsReadAsync(GetCurrentUserId());
            return Ok(new { message = "All notifications marked as read." });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(Guid id)
        {
            var result = await _notificationService.DeleteAsync(id);
            if (!result) return NotFound(new { message = "Notification not found." });
            return NoContent();
        }
    }
}
