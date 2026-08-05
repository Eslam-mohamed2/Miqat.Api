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

        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png" };
        private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5MB

        /// <summary>
        /// Only reads configuration here; the client is built on first actual use.
        ///
        /// It used to be constructed eagerly, and because UserController injects this
        /// service, DI built it on *every* request to that controller. With storage
        /// unconfigured the constructor threw, so GET /api/User/me — which never
        /// touches blob storage — failed with
        /// "Value cannot be null. (Parameter 'connectionString')", taking the whole
        /// user endpoint (and the sidebar profile card) down with it.
        ///
        /// The settings arrive as empty strings rather than null when present but
        /// blank, so the old `?? throw` never caught the real case.
        /// </summary>
        public BlobStorageService(IConfiguration configuration, ILogger<BlobStorageService> logger)
        {
            _logger = logger;
            _connectionString = configuration["AzureStorage:ConnectionString"];
            _containerName = configuration["AzureStorage:ContainerName"];
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

                return _cachedContainerClient ??=
                    new BlobServiceClient(_connectionString).GetBlobContainerClient(_containerName);
            }
        }

        public async Task<string> UploadImageAsync(IFormFile file)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("File is empty or null.", nameof(file));

            // Validate file extension
            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!AllowedExtensions.Contains(extension))
                throw new ArgumentException($"File type '{extension}' is not allowed. Only jpg, jpeg, png are supported.", nameof(file));

            // Validate file size
            if (file.Length > MaxFileSizeBytes)
                throw new ArgumentException($"File size exceeds maximum limit of {MaxFileSizeBytes / (1024 * 1024)}MB.", nameof(file));

            try
            {
                // Generate unique blob name: userId-timestamp-guid.ext
                var blobName = $"profile-images/{Guid.NewGuid()}-{DateTime.UtcNow:yyyyMMddHHmmss}{extension}";
                var blobClient = ContainerClient.GetBlobClient(blobName);

                // Upload file
                using (var stream = file.OpenReadStream())
                {
                    await blobClient.UploadAsync(stream, overwrite: true);
                }

                _logger.LogInformation("File uploaded successfully: {BlobName}", blobName);

                // Return blob URI
                return blobClient.Uri.AbsoluteUri;
            }
            catch (Azure.RequestFailedException ex)
            {
                _logger.LogError(ex, "Azure Blob Storage upload failed: {Message}", ex.Message);
                throw new InvalidOperationException("Failed to upload image to blob storage.", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during image upload: {Message}", ex.Message);
                throw;
            }
        }
    }
}
