using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Miqat.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.infrastructure.persistence.Data.Identity
{
    public class UserConfiguration : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.HasKey(user => user.Id);
            
            builder.Property(user => user.FullName)
                .IsRequired()
                .HasMaxLength(150);

            builder.Property(user => user.Email)
                .IsRequired()
                .HasMaxLength(255);

            builder.HasIndex(user => user.Email)
                .IsUnique();
            
            builder.Property(user => user.DateOfBirth)
                .IsRequired(false);

            builder.Property(user => user.Country)
                .IsRequired(false)
                .HasMaxLength(100);

            builder.Property(u => u.Gender)
                   .HasConversion<string>()
                   .IsRequired(false)
                   .HasMaxLength(150);

            builder.HasMany(user => user.Tasks)
            .WithOne(task => task.User)
            .HasForeignKey(task => task.UserId)
            .OnDelete(DeleteBehavior.Cascade);
        }

    }
}
