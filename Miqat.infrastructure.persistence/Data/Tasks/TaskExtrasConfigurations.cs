using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Miqat.Domain.Entities;

namespace Miqat.infrastructure.persistence.Data.Tasks;

public class SubtaskConfiguration : IEntityTypeConfiguration<Subtask>
{
    public void Configure(EntityTypeBuilder<Subtask> builder)
    {
        builder.ToTable("Subtasks");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Title).IsRequired().HasMaxLength(200);

        builder.HasOne(s => s.Task)
            .WithMany(t => t.Subtasks)
            .HasForeignKey(s => s.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        // A checklist is always read whole, in order.
        builder.HasIndex(s => new { s.TaskId, s.Position });
    }
}

public class TaskDependencyConfiguration : IEntityTypeConfiguration<TaskDependency>
{
    public void Configure(EntityTypeBuilder<TaskDependency> builder)
    {
        builder.ToTable("TaskDependencies");
        builder.HasKey(d => d.Id);

        builder.HasOne(d => d.Task)
            .WithMany(t => t.Prerequisites)
            .HasForeignKey(d => d.TaskId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.DependsOnTask)
            .WithMany()
            .HasForeignKey(d => d.DependsOnTaskId)
            .OnDelete(DeleteBehavior.Cascade);

        // The pair is the identity of a link. The second index answers
        // "which tasks wait for this one".
        builder.HasIndex(d => new { d.TaskId, d.DependsOnTaskId }).IsUnique();
        builder.HasIndex(d => d.DependsOnTaskId);
    }
}
