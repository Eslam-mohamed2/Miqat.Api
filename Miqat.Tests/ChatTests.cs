using Miqat.Domain.Entities;

namespace Miqat.Tests;

/// <summary>The rules a chat keeps by itself, whoever calls it.</summary>
public class ChatTests
{
    private static readonly Guid Ann = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Ben = Guid.Parse("00000000-0000-0000-0000-00000000000b");

    [Fact]
    public void A_pair_has_one_key_whoever_asks()
    {
        Assert.Equal(Conversation.DirectKeyFor(Ann, Ben), Conversation.DirectKeyFor(Ben, Ann));
        Assert.Equal(Conversation.DirectKeyFor(Ann, Ben), Conversation.Direct(Ben, Ann).DirectKey);
        Assert.NotEqual(Conversation.DirectKeyFor(Ann, Ben), Conversation.DirectKeyFor(Ann, Guid.NewGuid()));
    }

    [Fact]
    public void Nobody_chats_with_themselves()
    {
        Assert.Throws<ArgumentException>(() => Conversation.Direct(Ann, Ann));
    }

    [Fact]
    public void The_newest_message_time_only_moves_forward()
    {
        var chat = Conversation.Direct(Ann, Ben);
        var later = DateTime.UtcNow;

        chat.MessageSent(later);
        chat.MessageSent(later.AddMinutes(-5));

        Assert.Equal(later, chat.LastMessageAt);
    }

    [Fact]
    public void The_read_mark_only_moves_forward()
    {
        var me = new ConversationParticipant(Guid.NewGuid(), Ann);
        var now = DateTime.UtcNow;

        Assert.True(me.ReadUpTo(now));
        Assert.False(me.ReadUpTo(now.AddMinutes(-1)));
        Assert.False(me.ReadUpTo(now));
        Assert.Equal(now, me.LastReadAt);
    }

    [Fact]
    public void A_message_is_trimmed_and_never_empty_or_too_long()
    {
        Assert.Equal("hi there", new ChatMessage(Guid.NewGuid(), Ann, "  hi there \n").Body);

        Assert.Throws<ArgumentException>(() => new ChatMessage(Guid.NewGuid(), Ann, "   "));
        Assert.Throws<ArgumentException>(() => new ChatMessage(Guid.NewGuid(), Ann, new string('x', ChatMessage.MaxBodyLength + 1)));

        // The API answers 400 with this text, so it must not carry " (Parameter 'body')".
        var problem = Assert.Throws<ArgumentException>(() => new ChatMessage(Guid.NewGuid(), Ann, ""));
        Assert.Equal("A message cannot be empty.", problem.Message);
    }

    [Fact]
    public void Editing_marks_the_message_edited_only_when_the_text_changes()
    {
        var message = new ChatMessage(Guid.NewGuid(), Ann, "see you at 3");

        message.Edit("  see you at 3 ");
        Assert.Null(message.EditedAt);

        message.Edit("see you at 4");
        Assert.Equal("see you at 4", message.Body);
        Assert.NotNull(message.EditedAt);
    }

    [Fact]
    public void A_deleted_message_keeps_its_place_but_loses_its_text()
    {
        var message = new ChatMessage(Guid.NewGuid(), Ann, "oops, wrong chat");

        message.Remove();

        Assert.True(message.IsDeleted);
        Assert.Equal(string.Empty, message.Body);
        Assert.Throws<InvalidOperationException>(() => message.Edit("fixed"));
    }
}
