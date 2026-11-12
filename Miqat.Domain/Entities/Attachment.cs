using Miqat.Domain.Common;
using Miqat.Domain.Enumerations;

namespace Miqat.Domain.Entities;

/// <summary>A file attached to a task or an event. The bytes live in file storage under <see cref="BlobName"/>.</summary>
public class Attachment : BaseEntity
{
    public CalendarItemKind ItemKind { get; private set; }
    public Guid ItemId { get; private set; }

    public string FileName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public string BlobName { get; private set; } = string.Empty;

    public Guid UploadedByUserId { get; private set; }
    public virtual User UploadedBy { get; private set; } = null!;

    public Attachment(CalendarItemKind itemKind, Guid itemId, string fileName, string contentType,
                      long sizeBytes, string blobName, Guid uploadedByUserId)
    {
        ItemKind = itemKind;
        ItemId = itemId;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        BlobName = blobName;
        UploadedByUserId = uploadedByUserId;
    }

    private Attachment() { }
}
