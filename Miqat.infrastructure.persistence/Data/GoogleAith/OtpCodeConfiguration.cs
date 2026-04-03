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
    public class OtpCodeConfiguration : IEntityTypeConfiguration<OtpCode>
    {
        public void Configure(EntityTypeBuilder<OtpCode> builder)
        {
            builder.HasKey(o => o.Id);

            builder.Property(o => o.Code)
                .IsRequired()
                .HasMaxLength(10);

            builder.Property(o => o.Purpose)
                .IsRequired()
                .HasMaxLength(50);

            builder.Property(o => o.ExpiresAt)
                .IsRequired();

            builder.Property(o => o.IsUsed)
                .HasDefaultValue(false);

            builder.Ignore(o => o.IsExpired);
            builder.Ignore(o => o.IsValid);

            builder.HasOne(o => o.User)
                .WithMany(u => u.OtpCodes)
                .HasForeignKey(o => o.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
