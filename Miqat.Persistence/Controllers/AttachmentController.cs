using Microsoft.AspNetCore.Mvc;
using Miqat.Application.Interfaces;
using Miqat.Application.Services;
using Miqat.Domain.Enumerations;

namespace Miqat.API.Controllers
{
    /// <summary>
    /// Files attached to a task or an event. The same two addresses exist for
    /// both: /api/Task/{id}/attachments and /api/Event/{id}/attachments.
    /// </summary>
    [Route("api")]
    public class AttachmentController : BaseApiController
    {
        private const string OnItem = "{itemKind:" + ItemKindRouteConstraint.Name + "}/{itemId:guid}/attachments";

        private readonly IAttachmentService _attachments;

        public AttachmentController(IAttachmentService attachments) => _attachments = attachments;

        /// <param name="itemKind">Task or Event.</param>
        [HttpGet(OnItem)]
        public async Task<IActionResult> GetForItem(CalendarItemKind itemKind, Guid itemId)
            => Ok(await _attachments.GetForItemAsync(itemKind, itemId));

        /// <summary>Attaches a file (10 MB at most; documents, images, zip).</summary>
        /// <param name="itemKind">Task or Event.</param>
        [HttpPost(OnItem)]
        [RequestSizeLimit(AttachmentService.MaxFileBytes + 64 * 1024)] // the file plus multipart overhead
        public async Task<IActionResult> Add(CalendarItemKind itemKind, Guid itemId, IFormFile file)
        {
            var attachment = await _attachments.AddAsync(itemKind, itemId, file);
            return CreatedAtAction(nameof(GetForItem), new { itemKind, itemId }, attachment);
        }

        /// <summary>
        /// Downloads the file. It is only handed to someone who may see the
        /// task or event it is attached to.
        /// </summary>
        [HttpGet("Attachment/{id:guid}")]
        public async Task<IActionResult> Download(Guid id)
        {
            var file = await _attachments.OpenAsync(id);
            if (file == null) return NotFound(new { message = "Attachment not found." });

            // Always a download, never shown inside the page of the app.
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(file.Content, file.ContentType, file.FileName);
        }

        /// <summary>The person who attached a file can remove it; so can whoever may change the item.</summary>
        [HttpDelete("Attachment/{id:guid}")]
        public async Task<IActionResult> Delete(Guid id)
            => NoContentOrNotFound(await _attachments.DeleteAsync(id), "Attachment");
    }
}
