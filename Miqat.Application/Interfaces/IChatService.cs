using Miqat.Application.Modules;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Interfaces
{
    /// <summary>
    /// Chats between two friends and in groups: the messages, the read marks
    /// and the chat list. Every change is also pushed live to the people in
    /// the chat: "chatMessage", "chatMessageChanged" and "chatRead". Who is in
    /// a group is the business of <see cref="IGroupChatService"/>.
    /// </summary>
    public interface IChatService
    {
        /// <summary>The caller's chats that have messages, newest first.</summary>
        Task<List<ConversationDto>> GetConversationsAsync();

        /// <summary>Null when the chat does not exist; 403 when the caller is not in it.</summary>
        Task<ConversationDto?> GetConversationAsync(Guid conversationId);

        /// <summary>The chat with a friend, created the first time it is opened.</summary>
        Task<ConversationDto> OpenWithAsync(Guid friendId);

        /// <summary>A page of messages, oldest first: the newest ones, or the ones before <paramref name="beforeMessageId"/>.</summary>
        Task<ChatMessagePageDto?> GetMessagesAsync(Guid conversationId, Guid? beforeMessageId, int? limit);

        Task<ChatMessageDto?> SendAsync(Guid conversationId, SendMessageDto dto);

        /// <summary>Only the sender may edit or delete a message.</summary>
        Task<ChatMessageDto?> EditAsync(Guid messageId, EditMessageDto dto);
        Task<bool> DeleteAsync(Guid messageId);

        /// <summary>Marks everything in the chat as read by the caller.</summary>
        Task<bool> MarkReadAsync(Guid conversationId);

        Task<ChatUnreadDto> GetUnreadAsync();

        /// <summary>Ids of the caller's friends who have the app open right now.</summary>
        Task<List<Guid>> GetOnlineFriendIdsAsync();

        /// <summary>
        /// The other people of a chat, or none when <paramref name="userId"/> is
        /// not in it. Used by the live connection, which knows the user itself.
        /// </summary>
        Task<List<Guid>> GetOthersInAsync(Guid conversationId, Guid userId);

        /// <summary>Everyone in a chat; empty when it does not exist.</summary>
        Task<List<Guid>> GetParticipantsAsync(Guid conversationId);

        /// <summary>
        /// Writes a finished call into its chat as a message from the caller and
        /// shows it live to both sides. Called by the call service, never by a client.
        /// </summary>
        Task<ChatMessageDto> LogCallAsync(Guid conversationId, Guid callerId, bool video, CallOutcome outcome, int seconds);

        /// <summary>
        /// Writes a finished group call into its chat as a message from the one
        /// who started it; it is read for everyone who took part. Null when the
        /// group is gone. Called by the group call service, never by a client.
        /// </summary>
        Task<ChatMessageDto?> LogGroupCallAsync(Guid conversationId, Guid starterId, bool video, int seconds, IReadOnlyCollection<Guid> tookPart);

        /// <summary>
        /// Shows a message that was just saved to everyone in its chat
        /// ("chatMessage"). For the lines a change to a group leaves in the chat.
        /// </summary>
        Task<ChatMessageDto?> AnnounceAsync(Guid messageId);

        /// <summary>Tells everyone in a group chat who is in it and what it is called now ("chatGroupChanged").</summary>
        Task AnnounceGroupAsync(Guid conversationId);
    }

    /// <summary>
    /// Group chats: creating one, its name, and who is in it. Everything that
    /// happens leaves a line in the chat and is pushed live ("chatGroupChanged",
    /// and "chatGroupRemoved" to someone who is no longer in it).
    /// </summary>
    public interface IGroupChatService
    {
        /// <summary>A new group with the caller as its admin and at least two of their friends.</summary>
        Task<ConversationDto> CreateAsync(CreateGroupDto dto);

        /// <summary>Admins only.</summary>
        Task<ConversationDto> RenameAsync(Guid conversationId, RenameGroupDto dto);

        /// <summary>Any member adds friends of their own; people already in the group are skipped.</summary>
        Task<ConversationDto> AddMembersAsync(Guid conversationId, AddMembersDto dto);

        /// <summary>Admins only, and never oneself: that is leaving.</summary>
        Task<ConversationDto> RemoveMemberAsync(Guid conversationId, Guid userId);

        /// <summary>Admins only. The person stays an admin from then on.</summary>
        Task<ConversationDto> MakeAdminAsync(Guid conversationId, Guid userId);

        /// <summary>
        /// The caller leaves. When they were the last admin, the member who has
        /// been in the group longest becomes admin; when they were the last
        /// member, the group is deleted.
        /// </summary>
        Task LeaveAsync(Guid conversationId);
    }

    /// <summary>
    /// Who has the app open. Kept in the memory of this server, which is enough
    /// for one server; several servers behind a load balancer would need a
    /// shared store instead.
    /// </summary>
    public interface IPresenceTracker
    {
        bool IsOnline(Guid userId);

        /// <summary>A connection opened. True when it is the user's first: they just came online.</summary>
        bool Connected(Guid userId);

        /// <summary>A connection closed. True when it was the user's last: they just went offline.</summary>
        bool Disconnected(Guid userId);
    }
}
