using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Miqat.Domain.Entities;

namespace Miqat.infrastructure.persistence.Data.Calendars;

public class UserCalendarConfiguration : IEntityTypeConfiguration<UserCalendar>
{
    public void Configure(EntityTypeBuilder<UserCalendar> builder)
    {
        builder.ToTable("Calendars");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(80);
        builder.Property(c => c.Color).IsRequired().HasMaxLength(20);

        builder.HasOne(c => c.Owner)
            .WithMany()
            .HasForeignKey(c => c.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        // "My calendars" is the only way this table is read.
        builder.HasIndex(c => c.OwnerId);
    }
}

public class CalendarEventConfiguration : IEntityTypeConfiguration<CalendarEvent>
{
    public void Configure(EntityTypeBuilder<CalendarEvent> builder)
    {
        builder.ToTable("Events");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Title).IsRequired().HasMaxLength(200);
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.Location).HasMaxLength(300);
        builder.Property(e => e.ExternalUid).HasMaxLength(300);
        builder.Property(e => e.Recurrence).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.RecurrenceInterval).HasDefaultValue(1);
        builder.Ignore(e => e.IsRecurring);
        builder.Ignore(e => e.Duration);

        builder.HasOne(e => e.Owner)
            .WithMany()
            .HasForeignKey(e => e.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Events are moved to the default calendar before a calendar goes away.
        builder.HasOne(e => e.Calendar)
            .WithMany()
            .HasForeignKey(e => e.CalendarId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Group)
            .WithMany()
            .HasForeignKey(e => e.GroupId)
            .OnDelete(DeleteBehavior.SetNull);

        // The calendar asks for "my events that start before the end of the
        // month on screen"; the import asks for "my event with this UID".
        builder.HasIndex(e => new { e.OwnerId, e.StartsAt });
        builder.HasIndex(e => new { e.OwnerId, e.ExternalUid });
    }
}

public class EventAttendeeConfiguration : IEntityTypeConfiguration<EventAttendee>
{
    public void Configure(EntityTypeBuilder<EventAttendee> builder)
    {
        builder.ToTable("EventAttendees");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(a => a.Event)
            .WithMany(e => e.Attendees)
            .HasForeignKey(a => a.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // One row per person and event; "events I am invited to" by user.
        builder.HasIndex(a => new { a.EventId, a.UserId }).IsUnique();
        builder.HasIndex(a => new { a.UserId, a.Status });
    }
}

public class EventExceptionConfiguration : IEntityTypeConfiguration<EventException>
{
    public void Configure(EntityTypeBuilder<EventException> builder)
    {
        builder.ToTable("EventExceptions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.Location).HasMaxLength(300);

        builder.HasOne(x => x.Event)
            .WithMany(e => e.Exceptions)
            .HasForeignKey(x => x.EventId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.EventId, x.OccurrenceStart }).IsUnique();
    }
}

public class CalendarSettingsConfiguration : IEntityTypeConfiguration<CalendarSettings>
{
    public void Configure(EntityTypeBuilder<CalendarSettings> builder)
    {
        builder.ToTable("CalendarSettings");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.WeekStartsOn).HasConversion<string>().HasMaxLength(12);
        builder.Property(s => s.DefaultView).HasConversion<string>().HasMaxLength(12);
        builder.Ignore(s => s.WorkDays);

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => s.UserId).IsUnique();
    }
}

public class ReminderConfiguration : IEntityTypeConfiguration<Reminder>
{
    public void Configure(EntityTypeBuilder<Reminder> builder)
    {
        builder.ToTable("Reminders");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ItemKind).HasConversion<string>().HasMaxLength(10);
        builder.Ignore(r => r.IsSnoozed);

        builder.HasOne(r => r.User)
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // The reminder job: "everything whose time has come". Only rows that
        // still have something ahead are in the index, so it stays small.
        builder.HasIndex(r => r.RemindAt).HasFilter("\"RemindAt\" IS NOT NULL");

        // "The reminders on this task / event".
        builder.HasIndex(r => new { r.ItemKind, r.ItemId });
    }
}

public class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("Devices");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name).IsRequired().HasMaxLength(100);
        builder.Property(d => d.Endpoint).IsRequired().HasMaxLength(1000);
        builder.Property(d => d.P256dh).IsRequired().HasMaxLength(200);
        builder.Property(d => d.Auth).IsRequired().HasMaxLength(100);

        builder.HasOne(d => d.User)
            .WithMany()
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // A browser has one subscription; registering again updates the row.
        builder.HasIndex(d => d.Endpoint).IsUnique();
        builder.HasIndex(d => d.UserId);
    }
}

public class AttachmentConfiguration : IEntityTypeConfiguration<Attachment>
{
    public void Configure(EntityTypeBuilder<Attachment> builder)
    {
        builder.ToTable("Attachments");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ItemKind).HasConversion<string>().HasMaxLength(10);
        builder.Property(a => a.FileName).IsRequired().HasMaxLength(255);
        builder.Property(a => a.ContentType).IsRequired().HasMaxLength(100);
        builder.Property(a => a.BlobName).IsRequired().HasMaxLength(300);

        // Removing an account must not silently remove files from shared work.
        builder.HasOne(a => a.UploadedBy)
            .WithMany()
            .HasForeignKey(a => a.UploadedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.ItemKind, a.ItemId });
    }
}
