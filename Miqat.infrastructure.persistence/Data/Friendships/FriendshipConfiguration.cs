using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Miqat.Domain.Entities;

namespace Miqat.infrastructure.persistence.Data.Friendships
{
    public class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
    {
        public void Configure(EntityTypeBuilder<Friendship> builder)
        {
            builder.HasKey(f => f.Id);

            builder.Property(f => f.Status)
                .HasConversion<string>()
                .HasMaxLength(50);

            // Sender relationship
            builder.HasOne(f => f.Sender)
                .WithMany(u => u.FriendshipsSent)
                .HasForeignKey(f => f.SenderId)
                .OnDelete(DeleteBehavior.Cascade);

            // Receiver relationship
            builder.HasOne(f => f.Receiver)
                .WithMany(u => u.FriendshipsReceived)
                .HasForeignKey(f => f.ReceiverId)
                .OnDelete(DeleteBehavior.Cascade);

            // Indexes
            builder.HasIndex(f => f.SenderId);
            builder.HasIndex(f => f.ReceiverId);
            builder.HasIndex(f => new { f.SenderId, f.ReceiverId }).IsUnique();
            builder.HasIndex(f => f.Status);
        }
    }
}
