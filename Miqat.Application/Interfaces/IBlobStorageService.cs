using Microsoft.AspNetCore.Http;

namespace Miqat.Application.Interfaces
{
    public interface IBlobStorageService
    {
        /// <summary>
        /// Uploads an image file to Azure Blob Storage and returns the blob URI.
        /// </summary>
        /// <param name="file">The image file to upload (jpg, jpeg, png; max 5MB).</param>
        /// <returns>The public URI of the uploaded blob.</returns>
        /// <exception cref="ArgumentException">Thrown if file is invalid (unsupported format or size).</exception>
        /// <exception cref="InvalidOperationException">Thrown if upload fails.</exception>
        Task<string> UploadImageAsync(IFormFile file);
    }
}
