using Microsoft.AspNetCore.Http;

namespace Miqat.Application.Interfaces
{
    public interface IBlobStorageService
    {
        /// <summary>
        /// Whether image storage is actually configured. Lets callers answer with a
        /// clear "unavailable" rather than letting a missing connection string
        /// surface as a 500.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Uploads an image file to Azure Blob Storage and returns the blob URI.
        /// </summary>
        /// <param name="file">The image file to upload (jpg, jpeg, png; max 5MB).</param>
        /// <returns>The public URI of the uploaded blob.</returns>
        /// <exception cref="ArgumentException">Thrown if file is invalid (unsupported format or size).</exception>
        /// <exception cref="InvalidOperationException">Thrown if upload fails.</exception>
        Task<string> UploadImageAsync(IFormFile file);

        // ── Private files (attachments) ──────────────────────────────────────
        // Kept apart from the public images above: nothing here has a public
        // address, a file can only be read back through OpenFileAsync.

        /// <summary>Stores a file under a name chosen by the caller.</summary>
        Task SaveFileAsync(string name, Stream content, string contentType);

        /// <summary>The stored file, or null when there is none under that name.</summary>
        Task<Stream?> OpenFileAsync(string name);

        /// <summary>Removes a stored file. Does nothing when it is already gone; never throws.</summary>
        Task DeleteFileAsync(string name);
    }
}
