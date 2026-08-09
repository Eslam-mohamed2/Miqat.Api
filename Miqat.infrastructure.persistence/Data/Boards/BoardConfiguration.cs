using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Miqat.Domain.Entities;

namespace Miqat.infrastructure.persistence.Data.Boards
{
    public class BoardConfiguration : IEntityTypeConfiguration<Board>
    {
        public void Configure(EntityTypeBuilder<Board> builder)
        {
            builder.ToTable("Boards");
            builder.HasKey(b => b.Id);

            builder.Property(b => b.Name).IsRequired().HasMaxLength(120);
            builder.Property(b => b.Kind).HasConversion<string>().HasMaxLength(30).IsRequired();

            // jsonb rather than text: same storage cost here, but it lets the
            // drawing be queried later without a migration if that is ever needed.
            builder.Property(b => b.Content).HasColumnType("jsonb").IsRequired();

            builder.HasOne(b => b.Owner)
                .WithMany()
                .HasForeignKey(b => b.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            // The only query this table serves: "my boards of this kind, newest first".
            builder.HasIndex(b => new { b.OwnerId, b.Kind });
        }
    }
}
