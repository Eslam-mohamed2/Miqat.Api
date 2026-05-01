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
        private readonly BlobContainerClient _containerClient;
        private readonly ILogger<BlobStorageService> _logger;

        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png" };
        private const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5MB

        public BlobStorageService(IConfiguration configuration, ILogger<BlobStorageService> logger)
        {
            _logger = logger;

            var connectionString = configuration["AzureStorage:ConnectionString"]
                ?? throw new InvalidOperationException("AzureStorage:ConnectionString not configured.");
            var containerName = configuration["AzureStorage:ContainerName"]
                ?? throw new InvalidOperationException("AzureStorage:ContainerName not configured.");

            var blobServiceClient = new BlobServiceClient(connectionString);
            _containerClient = blobServiceClient.GetBlobContainerClient(containerName);
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
                var blobClient = _containerClient.GetBlobClient(blobName);

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
