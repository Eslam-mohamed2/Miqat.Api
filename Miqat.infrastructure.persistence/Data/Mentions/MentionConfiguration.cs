using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Miqat.Domain.Entities;

namespace Miqat.infrastructure.persistence.Data.Mentions
{
    public class MentionConfiguration : IEntityTypeConfiguration<Mention>
    {
        public void Configure(EntityTypeBuilder<Mention> builder)
        {
            builder.HasKey(m => m.Id);

            builder.Property(m => m.EntityType)
                .HasConversion<string>()
                .HasMaxLength(50);

            builder.Property(m => m.IsRead)
                .HasDefaultValue(false);

            // MentionedByUser relationship
            builder.HasOne(m => m.MentionedByUser)
                .WithMany(u => u.MentionsCreated)
                .HasForeignKey(m => m.MentionedByUserId)
                .OnDelete(DeleteBehavior.Cascade);

            // MentionedUser relationship
            builder.HasOne(m => m.MentionedUser)
                .WithMany(u => u.MentionsReceived)
                .HasForeignKey(m => m.MentionedUserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            builder.HasIndex(m => m.MentionedByUserId);
            builder.HasIndex(m => m.MentionedUserId);
            builder.HasIndex(m => m.EntityId);
            builder.HasIndex(m => new { m.MentionedUserId, m.IsRead });
        }
    }
}
