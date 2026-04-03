using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Miqat.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.infrastructure.persistence.Data.GoogleAith
{
    public class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
    {
        public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
        {
            builder.HasKey(p => p.Id);

            builder.Property(p => p.Token)
                .IsRequired()
                .HasMaxLength(500);

            builder.HasIndex(p => p.Token)
                .IsUnique();

            builder.Property(p => p.ExpiresAt)
                .IsRequired();

            builder.Property(p => p.IsUsed)
                .HasDefaultValue(false);

            builder.Ignore(p => p.IsExpired);
            builder.Ignore(p => p.IsValid);

            builder.HasOne(p => p.User)
                .WithMany(u => u.PasswordResetTokens)
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
