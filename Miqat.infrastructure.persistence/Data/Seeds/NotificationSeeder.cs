using Microsoft.EntityFrameworkCore;
using Miqat.Application.Interfaces;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.infrastructure.persistence.Data.Seeds
{
    public class NotificationSeeder : JsonSeederBase, ISeeder
    {
        private readonly MiqatDbContext _context;

        public NotificationSeeder(MiqatDbContext context) => _context = context;

        private record NotificationSeedDto(
            string Id,
            string Title,
            string Message,
            string Type,
            string RecipientUserId,
            string? TriggeredByUserId,
            bool IsRead
        );

        public async Task SeedAsync()
        {
            if (await _context.Notifications.AnyAsync()) return;

            var dtos = LoadJson<NotificationSeedDto>("notifications.json");

            var entities = dtos.Select(dto =>
            {
                var type = Enum.Parse<NotificationType>(dto.Type);

                var notification = new Notification(
                    dto.Title,
                    dto.Message,
                    type,
                    Guid.Parse(dto.RecipientUserId),
                    dto.TriggeredByUserId != null
                        ? Guid.Parse(dto.TriggeredByUserId)
                        : null
                );

                SetId(notification, Guid.Parse(dto.Id));
                return notification;
            }).ToList();

            await _context.Notifications.AddRangeAsync(entities);
            await _context.SaveChangesAsync();

            Console.WriteLine($"[Seeder] ✅ {entities.Count} notifications seeded.");
        }
    }
}
