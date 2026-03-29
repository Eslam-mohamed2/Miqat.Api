using Microsoft.EntityFrameworkCore;
using Miqat.Application.Interfaces;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;


namespace Miqat.infrastructure.persistence.Data.Seeds
{
    public class UserSeeder : JsonSeederBase, ISeeder
    {
        private readonly MiqatDbContext _context;

        public UserSeeder(MiqatDbContext context) => _context = context;

        private record UserSeedDto(
            string Id,
            string FullName,
            string Email,
            string PasswordPlain,
            string Gender,
            string Country,
            string PhoneNumber,
            string TimeZone,
            string DateOfBirth,
            string Role,
            bool IsActive,
            bool IsVerified
        );

        public async Task SeedAsync()
        {
            if (await _context.Users.AnyAsync()) return;

            var dtos = LoadJson<UserSeedDto>("users.json");

            var entities = dtos.Select(dto =>
            {
                var dob = DateTime.Parse(dto.DateOfBirth);
                var gender = Enum.Parse<Gender>(dto.Gender == "M" ? "Male" : "Female");
                var role = Enum.Parse<UserRole>(dto.Role);

                var user = new User(
                    dto.FullName,
                    dto.Email,
                    dob,
                    gender,
                    dto.Country,
                    dto.PhoneNumber,
                    dto.TimeZone
                );

                SetId(user, Guid.Parse(dto.Id));
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.PasswordPlain);
                user.Role = role;
                user.IsActive = dto.IsActive;
                user.IsVerified = dto.IsVerified;

                return user;
            }).ToList();

            await _context.Users.AddRangeAsync(entities);
            await _context.SaveChangesAsync();

            Console.WriteLine($"[Seeder] ✅ {entities.Count} users seeded.");
        }
    }
}
