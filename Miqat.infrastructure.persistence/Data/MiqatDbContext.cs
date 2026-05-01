using Microsoft.EntityFrameworkCore;
using Miqat.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Miqat.infrastructure.persistence.Data
{
    public class MiqatDbContext : DbContext
    {
        public MiqatDbContext(DbContextOptions<MiqatDbContext> options)
            : base(options) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<TaskItem> Tasks => Set<TaskItem>();
        public DbSet<Group> Groups => Set<Group>();
        public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
        public DbSet<Notification> Notifications => Set<Notification>();
        public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
        public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
        public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
        public DbSet<Friendship> Friendships => Set<Friendship>();
        public DbSet<Mention> Mentions => Set<Mention>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Auto-picks up ALL IEntityTypeConfiguration classes in this assembly
            modelBuilder.ApplyConfigurationsFromAssembly(
                typeof(MiqatDbContext).Assembly);
        }
    }
}
