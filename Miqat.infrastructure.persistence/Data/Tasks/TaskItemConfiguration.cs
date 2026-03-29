using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.infrastructure.persistence.Data.Tasks
{
    public class TaskItemConfiguration : IEntityTypeConfiguration<TaskItem>
    {
        public void Configure(EntityTypeBuilder<TaskItem> builder)
        {
            builder.HasKey(t => t.Id);

            builder.Property(t => t.Title)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(t => t.Description)
                .HasMaxLength(1000)
                .IsRequired(false);

            builder.Property(t => t.Status)
                .HasConversion<string>()
                .HasDefaultValue(Domain.Enumerations.TaskStatus.Pending)
                .HasMaxLength(50);

            builder.Property(t => t.Priority)
                    .HasConversion<string>()
                    .HasMaxLength(50);

            builder.Property(t => t.Recurrence)
                .HasConversion<string>()
                .HasDefaultValue(RecurrencePattern.None)
                .HasMaxLength(50);

            builder.Property(t => t.DueDate)
                .IsRequired(false);

            builder.Property(t => t.RecurrenceEndDate)
                .IsRequired(false);

            builder.Property(t => t.Tags)
                .HasMaxLength(500)
                .IsRequired(false);

            // Owner
            builder.HasOne(t => t.User)
                .WithMany(u => u.Tasks)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Assigned to
            builder.HasOne(t => t.AssignedToUser)
                .WithMany()
                .HasForeignKey(t => t.AssignedToUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);
        }
    }
}
