using Microsoft.EntityFrameworkCore;
using Miqat.Application.Interfaces;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.infrastructure.persistence.Data.Seeds
{
    public class FriendshipSeeder : JsonSeederBase, ISeeder
    {
        private readonly MiqatDbContext _context;

        public FriendshipSeeder(MiqatDbContext context) => _context = context;

        private record FriendshipSeedDto(
            string Id,
            string SenderId,
            string ReceiverId,
            string Status
        );

        public async Task SeedAsync()
        {
            if (await _context.Friendships.AnyAsync()) return;

            var dtos = LoadJson<FriendshipSeedDto>("friendships.json");

            var entities = dtos.Select(dto =>
            {
                var status = Enum.Parse<FriendshipStatus>(dto.Status);

                var friendship = new Friendship(
                    Guid.Parse(dto.SenderId),
                    Guid.Parse(dto.ReceiverId)
                );

                SetId(friendship, Guid.Parse(dto.Id));
                friendship.Status = status;

                return friendship;
            }).ToList();

            await _context.Friendships.AddRangeAsync(entities);
            await _context.SaveChangesAsync();

            Console.WriteLine($"[Seeder] ✅ {entities.Count} friendships seeded.");
        }
    }
}
