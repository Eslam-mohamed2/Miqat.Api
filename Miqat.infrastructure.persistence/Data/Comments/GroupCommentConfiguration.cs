using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Miqat.Domain.Entities;

namespace Miqat.infrastructure.persistence.Data.Comments
{
    public class GroupCommentConfiguration : IEntityTypeConfiguration<GroupComment>
    {
        public void Configure(EntityTypeBuilder<GroupComment> builder)
        {
            builder.ToTable("GroupComments");
            builder.HasKey(c => c.Id);

            builder.Property(c => c.Content)
                .IsRequired()
                .HasMaxLength(GroupComment.MaxContentLength);

            // Deleting a project takes its discussion with it.
            builder.HasOne(c => c.Group)
                .WithMany()
                .HasForeignKey(c => c.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            // As for task comments: removing an account must not erase a team's
            // conversation history.
            builder.HasOne(c => c.Author)
                .WithMany()
                .HasForeignKey(c => c.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);

            // A reply goes with the comment it answers.
            builder.HasOne(c => c.Parent)
                .WithMany()
                .HasForeignKey(c => c.ParentId)
                .OnDelete(DeleteBehavior.Cascade);

            // "The newest comments of this project" and "the replies to these comments".
            builder.HasIndex(c => new { c.GroupId, c.CreatedAt });
            builder.HasIndex(c => c.ParentId);
            builder.HasIndex(c => c.AuthorId);
        }
    }
}
