using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Miqat.Application.Interfaces;

namespace Miqat.infrastructure.persistence.Services
{
    public class BlobStorageService : IBlobStorageService
    {
        private readonly ILogger<BlobStorageService> _logger;
        private readonly string? _connectionString;
        private readonly string? _containerName;
        private BlobContainerClient? _cachedContainerClient;

        private readonly string _filesContainerName;
        private BlobContainerClient? _filesContainerClient;

        // Extension -> the content type the blob is stored with. Without an
        // explicit type the blob is served as application/octet-stream.
        private static readonly Dictionary<string, string> AllowedTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png"
        };

        private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5MB

        /// <summary>
        /// Only reads configuration here; the client is built on first actual use.
        ///
        /// It used to be constructed eagerly, and because UserController injects this
        /// service, DI built it on *every* request to that controller. With storage
        /// unconfigured the constructor threw, so GET /api/User/me — which never
        /// touches blob storage — failed, taking the whole user endpoint down with it.
        ///
        /// The settings arrive as empty strings rather than null when present but
        /// blank, so the old `?? throw` never caught the real case.
        /// </summary>
        public BlobStorageService(IConfiguration configuration, ILogger<BlobStorageService> logger)
        {
            _logger = logger;
            _connectionString = configuration["AzureStorage:ConnectionString"];
            _containerName = configuration["AzureStorage:ContainerName"];

            var filesContainer = configuration["AzureStorage:FilesContainerName"];
            _filesContainerName = string.IsNullOrWhiteSpace(filesContainer) ? "attachments" : filesContainer;
        }

        /// <summary>True when image upload is actually available.</summary>
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_connectionString) && !string.IsNullOrWhiteSpace(_containerName);

        private BlobContainerClient ContainerClient
        {
            get
            {
                if (!IsConfigured)
                    throw new InvalidOperationException(
                        "Image upload is unavailable: AzureStorage:ConnectionString / ContainerName are not configured.");

                try
                {
                    return _cachedContainerClient ??=
                        new BlobServiceClient(_connectionString).GetBlobContainerClient(_containerName);
                }
                catch (Exception ex) when (ex is FormatException or ArgumentException)
                {
                    _logger.LogError(ex, "AzureStorage:ConnectionString is not a valid connection string.");
                    throw new InvalidOperationException(
                        "Image upload is unavailable: the storage connection string is not valid.", ex);
                }
            }
        }

        public async Task<string> UploadImageAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("File is empty or null.", nameof(file));

            var extension = Path.GetExtension(file.FileName);
            if (string.IsNullOrEmpty(extension) || !AllowedTypes.TryGetValue(extension, out var contentType))
                throw new ArgumentException("Only jpg, jpeg and png images are supported.", nameof(file));

            if (file.Length > MaxFileSizeBytes)
                throw new ArgumentException($"File size exceeds maximum limit of {MaxFileSizeBytes / (1024 * 1024)}MB.", nameof(file));

            await using var stream = file.OpenReadStream();

            // The extension is chosen by the client, so check the real content too.
            if (!await LooksLikeImageAsync(stream, contentType))
                throw new ArgumentException("The file is not a valid jpg or png image.", nameof(file));

            var container = ContainerClient;

            try
            {
                var blobName = $"profile-images/{Guid.NewGuid()}-{DateTime.UtcNow:yyyyMMddHHmmss}{extension.ToLowerInvariant()}";
                var blobClient = container.GetBlobClient(blobName);

                await blobClient.UploadAsync(stream, new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
                });

                _logger.LogInformation("File uploaded successfully: {BlobName}", blobName);

                return blobClient.Uri.AbsoluteUri;
            }
            catch (Azure.RequestFailedException ex)
            {
                _logger.LogError(ex, "Azure Blob Storage upload failed: {Message}", ex.Message);
                throw new InvalidOperationException("Failed to upload image to blob storage.", ex);
            }
        }

        // ── Private files (attachments) ──────────────────────────────────────

        /// <summary>
        /// The container attachments live in: separate from the public image
        /// container, created without public access the first time it is used.
        /// </summary>
        private async Task<BlobContainerClient> GetFilesContainerAsync()
        {
            if (_filesContainerClient != null) return _filesContainerClient;

            if (!IsConfigured)
                throw new InvalidOperationException(
                    "File storage is unavailable: AzureStorage:ConnectionString is not configured.");

            var container = new BlobServiceClient(_connectionString).GetBlobContainerClient(_filesContainerName);
            await container.CreateIfNotExistsAsync(PublicAccessType.None);
            return _filesContainerClient = container;
        }

        public async Task SaveFileAsync(string name, Stream content, string contentType)
        {
            var container = await GetFilesContainerAsync();

            await container.GetBlobClient(name).UploadAsync(content, new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders { ContentType = contentType }
            });
        }

        public async Task<Stream?> OpenFileAsync(string name)
        {
            var container = await GetFilesContainerAsync();

            try
            {
                return await container.GetBlobClient(name).OpenReadAsync();
            }
            catch (Azure.RequestFailedException ex) when (ex.Status == 404)
            {
                return null;
            }
        }

        public async Task DeleteFileAsync(string name)
        {
            try
            {
                var container = await GetFilesContainerAsync();
                await container.GetBlobClient(name).DeleteIfExistsAsync();
            }
            catch (Exception ex)
            {
                // The row that pointed at the file is already gone; a file left
                // behind is only wasted space.
                _logger.LogWarning(ex, "Could not delete stored file {Name}.", name);
            }
        }

        /// <summary>Checks the file signature (magic bytes) and rewinds the stream.</summary>
        private static async Task<bool> LooksLikeImageAsync(Stream stream, string contentType)
        {
            var header = new byte[8];
            var read = await stream.ReadAsync(header.AsMemory(0, header.Length));
            stream.Position = 0;

            if (contentType == "image/png")
                return read >= 8 &&
                       header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
                       header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;

            // JPEG
            return read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
        }
    }
}
