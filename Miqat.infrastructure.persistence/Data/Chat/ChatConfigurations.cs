using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Miqat.Domain.Entities;

namespace Miqat.infrastructure.persistence.Data.Chat;

public class ConversationConfiguration : IEntityTypeConfiguration<Conversation>
{
    public void Configure(EntityTypeBuilder<Conversation> builder)
    {
        builder.ToTable("Conversations");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.DirectKey).HasMaxLength(65);
        builder.Property(c => c.Name).HasMaxLength(Conversation.MaxNameLength);

        // One chat per pair of people. Group chats (no key) are not affected.
        builder.HasIndex(c => c.DirectKey).IsUnique().HasFilter("\"DirectKey\" IS NOT NULL");

        // The group outlives the account of the person who created it.
        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(c => c.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public class ConversationParticipantConfiguration : IEntityTypeConfiguration<ConversationParticipant>
{
    public void Configure(EntityTypeBuilder<ConversationParticipant> builder)
    {
        builder.ToTable("ConversationParticipants");
        builder.HasKey(p => p.Id);

        builder.HasOne(p => p.Conversation)
            .WithMany(c => c.Participants)
            .HasForeignKey(p => p.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.User)
            .WithMany()
            .HasForeignKey(p => p.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // "Is this person in this chat" and "the chats of this person".
        builder.HasIndex(p => new { p.ConversationId, p.UserId }).IsUnique();
        builder.HasIndex(p => p.UserId);
    }
}

public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("ChatMessages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Body).IsRequired().HasMaxLength(ChatMessage.MaxBodyLength);

        // Stored as its name, like the other enums of this database.
        builder.Property(m => m.CallOutcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.GroupEvent).HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.GroupName).HasMaxLength(Conversation.MaxNameLength);
        builder.Ignore(m => m.IsCall);
        builder.Ignore(m => m.IsGroupEvent);

        // A line about a person goes when that person's account is removed for good.
        builder.HasOne(m => m.TargetUser)
            .WithMany()
            .HasForeignKey(m => m.TargetUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.Sender)
            .WithMany()
            .HasForeignKey(m => m.SenderId)
            .OnDelete(DeleteBehavior.Cascade);

        // A chat is read newest first, a page at a time.
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAt });
    }
}
