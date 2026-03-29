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
    public class TaskSeeder : JsonSeederBase, ISeeder
    {
        private readonly MiqatDbContext _context;

        public TaskSeeder(MiqatDbContext context) => _context = context;

        private record TaskSeedDto(
            string Id,
            string Title,
            string Description,
            string UserId,
            string? AssignedToUserId,
            string? GroupId,
            string Priority,
            string Status,
            string DueDate,
            string Tags,
            string Recurrence,
            string? RecurrenceEndDate
        );

        public async Task SeedAsync()
        {
            if (await _context.Tasks.AnyAsync()) return;

            var dtos = LoadJson<TaskSeedDto>("tasks.json");

            var entities = dtos.Select(dto =>
            {
                var priority = Enum.Parse<Priority>(dto.Priority);
                var recurrence = Enum.Parse<RecurrencePattern>(dto.Recurrence);

                var task = new TaskItem(
                    dto.Title,
                    dto.Description,
                    Guid.Parse(dto.UserId),
                    priority,
                    DateTime.Parse(dto.DueDate),
                    dto.AssignedToUserId != null ? Guid.Parse(dto.AssignedToUserId) : null,
                    dto.GroupId != null ? Guid.Parse(dto.GroupId) : null,
                    dto.Tags,
                    recurrence,
                    dto.RecurrenceEndDate != null ? DateTime.Parse(dto.RecurrenceEndDate) : null
                );

                SetId(task, Guid.Parse(dto.Id));
                return task;
            }).ToList();

            await _context.Tasks.AddRangeAsync(entities);
            await _context.SaveChangesAsync();

            Console.WriteLine($"[Seeder] ✅ {entities.Count} tasks seeded.");
        }
    }
}
