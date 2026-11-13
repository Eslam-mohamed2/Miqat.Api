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
        public DbSet<Comment> Comments => Set<Comment>();
        public DbSet<Board> Boards => Set<Board>();
        public DbSet<UserCalendar> Calendars => Set<UserCalendar>();
        public DbSet<CalendarEvent> Events => Set<CalendarEvent>();
        public DbSet<EventAttendee> EventAttendees => Set<EventAttendee>();
        public DbSet<EventException> EventExceptions => Set<EventException>();
        public DbSet<CalendarSettings> CalendarSettings => Set<CalendarSettings>();
        public DbSet<Reminder> Reminders => Set<Reminder>();
        public DbSet<Device> Devices => Set<Device>();
        public DbSet<Subtask> Subtasks => Set<Subtask>();
        public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();
        public DbSet<Attachment> Attachments => Set<Attachment>();
        public DbSet<Conversation> Conversations => Set<Conversation>();
        public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();
        public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
        public DbSet<GroupComment> GroupComments => Set<GroupComment>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Auto-picks up ALL IEntityTypeConfiguration classes in this assembly
            modelBuilder.ApplyConfigurationsFromAssembly(
                typeof(MiqatDbContext).Assembly);
        }
    }
}
