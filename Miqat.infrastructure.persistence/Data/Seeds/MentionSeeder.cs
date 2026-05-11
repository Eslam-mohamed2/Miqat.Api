using Microsoft.EntityFrameworkCore;
using Miqat.Application.Interfaces;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.infrastructure.persistence.Data.Seeds
{
    public class MentionSeeder : JsonSeederBase, ISeeder
    {
        private readonly MiqatDbContext _context;

        public MentionSeeder(MiqatDbContext context) => _context = context;

        private record MentionSeedDto(
            string Id,
            string MentionedByUserId,
            string MentionedUserId,
            string EntityType,
            string EntityId,
            bool IsRead
        );

        public async Task SeedAsync()
        {
            if (await _context.Mentions.AnyAsync()) return;

            var dtos = LoadJson<MentionSeedDto>("mentions.json");

            var entities = dtos.Select(dto =>
            {
                var entityType = Enum.Parse<EntityType>(dto.EntityType);

                var mention = new Mention(
                    Guid.Parse(dto.MentionedByUserId),
                    Guid.Parse(dto.MentionedUserId),
                    entityType,
                    Guid.Parse(dto.EntityId)
                );

                SetId(mention, Guid.Parse(dto.Id));
                mention.IsRead = dto.IsRead;

                return mention;
            }).ToList();

            await _context.Mentions.AddRangeAsync(entities);
            await _context.SaveChangesAsync();

            Console.WriteLine($"[Seeder] ✅ {entities.Count} mentions seeded.");
        }
    }
}
