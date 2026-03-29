using System;
using System.Collections.Generic;
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

        protected static void SetId(object entity, Guid id) =>
            entity.GetType()
                  .BaseType!
                  .GetProperty("Id")!
                  .SetValue(entity, id);
    }
}
