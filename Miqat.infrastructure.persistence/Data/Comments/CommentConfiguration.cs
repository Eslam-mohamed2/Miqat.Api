using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Miqat.Domain.Entities;

namespace Miqat.infrastructure.persistence.Data.Comments
{
    public class CommentConfiguration : IEntityTypeConfiguration<Comment>
    {
        public void Configure(EntityTypeBuilder<Comment> builder)
        {
            builder.HasKey(c => c.Id);

            builder.Property(c => c.Content)
                .IsRequired()
                .HasMaxLength(2000);

            // Deleting a task takes its discussion with it.
            builder.HasOne(c => c.Task)
                .WithMany()
                .HasForeignKey(c => c.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            // Deleting a user must not silently erase a team's conversation
            // history, so the relationship is restricted instead.
            builder.HasOne(c => c.Author)
                .WithMany()
                .HasForeignKey(c => c.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(c => c.TaskId);
        }
    }
}
