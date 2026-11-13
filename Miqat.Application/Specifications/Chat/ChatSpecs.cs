using Miqat.Domain.Entities;
using Miqat.Domain.Specifications;

namespace Miqat.Application.Specifications.Chat
{
    /// <summary>
    /// A page of a chat, newest first: the newest messages, or the ones sent
    /// before a given moment. Deleted messages are included; they keep their
    /// place in the chat. Each message comes with the people it names.
    /// </summary>
    public class ChatMessagesPageSpec : BaseSpecification<ChatMessage>
    {
        public ChatMessagesPageSpec(Guid conversationId, DateTime? before, int take)
            : base(m => m.ConversationId == conversationId && (before == null || m.CreatedAt < before))
        {
            AddInclude(m => m.Sender);
            AddInclude(m => m.TargetUser!);
            AddOrderByDescending(m => m.CreatedAt);
            ApplyPaging(0, take);
        }
    }
}
