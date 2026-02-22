using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Miqat.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.infrastructure.persistence.Data.Tasks
{
    public class TaskItemConfiguration :IEntityTypeConfiguration<TaskItem>
    {
        public void Configure(EntityTypeBuilder<TaskItem> builder)
        {
            builder.HasKey(task => task.Id);
           
            builder.Property(task => task.Title)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(task => task.Description)
                .HasMaxLength(1000)
                .IsRequired(false);
            builder.Property(task => task.Status)
                .HasConversion<string>()
                .HasDefaultValue(Miqat.Domain.Enumerations.TaskStatus.Pending)
                .HasMaxLength(50);

            builder.HasOne(task => task.User)      
                .WithMany(user => user.Tasks)     
                .HasForeignKey(task => task.UserId) 
                .OnDelete(DeleteBehavior.Cascade);  

        }
    }
}
