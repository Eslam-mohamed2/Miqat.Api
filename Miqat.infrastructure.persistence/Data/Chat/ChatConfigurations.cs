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

        // One chat per pair of people. Group chats (no key) are not affected.
        builder.HasIndex(c => c.DirectKey).IsUnique().HasFilter("\"DirectKey\" IS NOT NULL");
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
        builder.Ignore(m => m.IsCall);

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
