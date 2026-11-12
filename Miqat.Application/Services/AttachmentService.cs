using Microsoft.AspNetCore.Http;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Services
{
    /// <inheritdoc />
    /// <remarks>
    /// Files are kept in private storage and only ever handed out through this
    /// service, after checking that the caller may see the task or event the
    /// file is attached to. There is no public link to guess.
    /// </remarks>
    public class AttachmentService : IAttachmentService
    {
        public const long MaxFileBytes = 10 * 1024 * 1024;

        private const int MaxPerItem = 20;
        private const int MaxFileNameLength = 255; // Attachments.FileName column size

        /// <summary>
        /// What may be attached, and the type each file is stored and served
        /// with. The type comes from this list, never from the upload, and
        /// nothing a browser would run (html, svg, scripts) is on it.
        /// </summary>
        private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".txt"] = "text/plain",
            [".csv"] = "text/csv",
            [".md"] = "text/markdown",
            [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".ppt"] = "application/vnd.ms-powerpoint",
            [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            [".zip"] = "application/zip"
        };

        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly IAccessPolicy _access;
        private readonly IBlobStorageService _storage;

        public AttachmentService(
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUser,
            IAccessPolicy access,
            IBlobStorageService storage)
        {
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _access = access;
            _storage = storage;
        }

        private IGenericRepository<Attachment> Attachments => _unitOfWork.Repository<Attachment>();

        public async Task<List<AttachmentDto>> GetForItemAsync(CalendarItemKind kind, Guid itemId)
        {
            await _access.RequireItemAsync(kind, itemId);

            var rows = await Attachments.SelectAsync(
                a => a.ItemKind == kind && a.ItemId == itemId,
                a => new { Attachment = a, UploaderName = a.UploadedBy.FullName });

            return rows
                .OrderBy(r => r.Attachment.CreatedAt)
                .Select(r => ToDto(r.Attachment, r.UploaderName))
                .ToList();
        }

        public async Task<AttachmentDto> AddAsync(CalendarItemKind kind, Guid itemId, IFormFile file)
        {
            await _access.RequireItemAsync(kind, itemId, toChange: true);

            // Storage that is not set up is a state of the deployment, not a
            // fault: it answers 503 with something the screen can show.
            if (!_storage.IsConfigured)
                throw new ApiException("Attachments are not available: file storage is not configured.", 503);

            if (file == null || file.Length == 0)
                throw new ApiException("Choose a file to attach.", 400);
            if (file.Length > MaxFileBytes)
                throw new ApiException($"A file can be up to {MaxFileBytes / (1024 * 1024)} MB.", 400);

            // The name is chosen by the client: keep only its last part.
            var fileName = Path.GetFileName(file.FileName ?? string.Empty);
            var extension = Path.GetExtension(fileName);
            if (!AllowedTypes.TryGetValue(extension, out var contentType))
                throw new ApiException(
                    "That kind of file cannot be attached. Allowed: " + string.Join(", ", AllowedTypes.Keys.Select(k => k[1..])) + ".", 400);

            if (await Attachments.CountAsync(a => a.ItemKind == kind && a.ItemId == itemId) >= MaxPerItem)
                throw new ApiException($"Up to {MaxPerItem} files can be attached to one item.", 400);

            var userId = _currentUser.RequireUserId();
            var blobName = $"{kind.ToString().ToLowerInvariant()}/{itemId}/{Guid.NewGuid()}{extension.ToLowerInvariant()}";

            await using (var content = file.OpenReadStream())
                await _storage.SaveFileAsync(blobName, content, contentType);

            var attachment = new Attachment(
                kind, itemId,
                fileName.Length <= MaxFileNameLength ? fileName : fileName[^MaxFileNameLength..],
                contentType, file.Length, blobName, userId);

            await Attachments.AddAsync(attachment);
            await _unitOfWork.CompleteAsync();

            var uploader = await _unitOfWork.Repository<User>().GetByIdAsync(userId);
            return ToDto(attachment, uploader?.FullName);
        }

        public async Task<AttachmentFile?> OpenAsync(Guid attachmentId)
        {
            var attachment = await Attachments.GetByIdAsync(attachmentId);
            if (attachment == null) return null;

            await _access.RequireItemAsync(attachment.ItemKind, attachment.ItemId);

            var content = await _storage.OpenFileAsync(attachment.BlobName);
            return content == null ? null : new AttachmentFile(attachment.FileName, attachment.ContentType, content);
        }

        public async Task<bool> DeleteAsync(Guid attachmentId)
        {
            var attachment = await Attachments.GetByIdAsync(attachmentId);
            if (attachment == null) return false;

            // People may always take back what they attached themselves; removing
            // the file of someone else takes the right to change the item.
            var isMine = attachment.UploadedByUserId == _currentUser.RequireUserId();
            await _access.RequireItemAsync(attachment.ItemKind, attachment.ItemId, toChange: !isMine);

            Attachments.Delete(attachment);
            await _unitOfWork.CompleteAsync();

            // After the row: a file without a row is only wasted space, a row
            // without a file would be a broken download.
            await _storage.DeleteFileAsync(attachment.BlobName);
            return true;
        }

        private static AttachmentDto ToDto(Attachment attachment, string? uploaderName) => new()
        {
            Id = attachment.Id,
            ItemKind = attachment.ItemKind,
            ItemId = attachment.ItemId,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            SizeBytes = attachment.SizeBytes,
            UploadedByUserId = attachment.UploadedByUserId,
            UploadedByName = uploaderName,
            CreatedAt = attachment.CreatedAt,
            DownloadUrl = $"/api/Attachment/{attachment.Id}"
        };
    }
}
