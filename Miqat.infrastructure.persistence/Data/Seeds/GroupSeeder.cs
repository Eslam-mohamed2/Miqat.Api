using Microsoft.EntityFrameworkCore;
using Miqat.Application.Interfaces;
using Miqat.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.infrastructure.persistence.Data.Seeds
{
    public class GroupSeeder : JsonSeederBase, ISeeder
    {
        private readonly MiqatDbContext _context;

        public GroupSeeder(MiqatDbContext context) => _context = context;

        private record GroupSeedDto(
            string Id,
            string Name,
            string Description,
            string OwnerId,
            string Color,
            List<string> Members
        );

        public async Task SeedAsync()
        {
            if (await _context.Groups.AnyAsync()) return;

            var dtos = LoadJson<GroupSeedDto>("groups.json");

            var groups = dtos.Select(dto =>
            {
                var group = new Group(
                    dto.Name,
                    dto.Description,
                    Guid.Parse(dto.OwnerId),
                    dto.Color
                );
                SetId(group, Guid.Parse(dto.Id));
                return group;
            }).ToList();

            await _context.Groups.AddRangeAsync(groups);
            await _context.SaveChangesAsync();

            var members = dtos.SelectMany(dto =>
                dto.Members.Select(memberId =>
                {
                    var gm = new GroupMember(
                        Guid.Parse(dto.Id),
                        Guid.Parse(memberId)
                    );
                    SetId(gm, Guid.NewGuid());
                    return gm;
                })
            ).ToList();

            await _context.GroupMembers.AddRangeAsync(members);
            await _context.SaveChangesAsync();

            Console.WriteLine($"[Seeder] ✅ {groups.Count} groups + {members.Count} memberships seeded.");
        }
    }
}
