using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Miqat.infrastructure.persistence.Data.Seeds
{
    public abstract class JsonSeederBase
    {
        protected static List<T> LoadJson<T>(string fileName)
        {
            var basePath = AppContext.BaseDirectory;
            var filePath = Path.Combine(basePath, "SeedData", fileName);

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Seed file not found: {filePath}");

            var json = File.ReadAllText(filePath);

            return JsonSerializer.Deserialize<List<T>>(json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new List<T>();
        }

        /// <summary>
        /// Parses a seed timestamp as UTC.
        ///
        /// The seed JSON carries dates as plain strings with no zone offset, and a
        /// bare DateTime.Parse returns DateTimeKind.Unspecified. Npgsql refuses to
        /// write those to a `timestamp with time zone` column, so every seeder threw
        /// and the database came up empty. These values are UTC, so say so.
        /// </summary>
        protected static DateTime ParseUtc(string value) =>
            DateTime.Parse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        /// <inheritdoc cref="ParseUtc(string)"/>
        protected static DateTime? ParseUtcOrNull(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : ParseUtc(value);

        protected static void SetId(object entity, Guid id) =>
            entity.GetType()
                  .BaseType!
                  .GetProperty("Id")!
                  .SetValue(entity, id);
    }
}
