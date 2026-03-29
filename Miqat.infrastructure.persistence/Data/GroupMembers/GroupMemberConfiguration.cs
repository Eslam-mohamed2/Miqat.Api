using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Miqat.Domain.Entities;


namespace Miqat.infrastructure.persistence.Data.GroupMembers
{
    public class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
    {
        public void Configure(EntityTypeBuilder<GroupMember> builder)
        {
            builder.HasKey(gm => gm.Id);

            // No duplicate memberships
            builder.HasIndex(gm => new { gm.GroupId, gm.UserId })
                .IsUnique();

            builder.Property(gm => gm.JoinedAt)
                .IsRequired();

            builder.HasOne(gm => gm.User)
                .WithMany(u => u.GroupMembers)
                .HasForeignKey(gm => gm.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
