using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;

namespace Miqat.API.Controllers
{
    /// <summary>
    /// Chats between two friends and in groups. Live updates arrive on the
    /// hub (/hubs/miqat): "chatMessage", "chatMessageChanged", "chatRead",
    /// "chatTyping" and "presenceChanged", and for groups "chatGroupChanged"
    /// and "chatGroupRemoved". While typing, the app calls the hub method
    /// "Typing" with the chat id.
    /// </summary>
    public class ChatController : BaseApiController
    {
        private readonly IChatService _chat;
        private readonly IGroupChatService _groups;

        public ChatController(IChatService chat, IGroupChatService groups)
        {
            _chat = chat;
            _groups = groups;
        }

        /// <summary>My chats that have messages, newest first, with the unread count of each.</summary>
        [HttpGet]
        public async Task<IActionResult> GetConversations()
            => Ok(await _chat.GetConversationsAsync());

        /// <summary>For the badge: how many chats and messages are unread.</summary>
        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
            => Ok(await _chat.GetUnreadAsync());

        /// <summary>Ids of my friends who have the app open right now.</summary>
        [HttpGet("online")]
        public async Task<IActionResult> GetOnlineFriends()
            => Ok(await _chat.GetOnlineFriendIdsAsync());

        /// <summary>Opens the chat with a friend; it is created the first time.</summary>
        [HttpPost("with/{userId:guid}")]
        public async Task<IActionResult> OpenWith(Guid userId)
            => Ok(await _chat.OpenWithAsync(userId));

        [HttpGet("{conversationId:guid}")]
        public async Task<IActionResult> GetConversation(Guid conversationId)
            => OkOrNotFound(await _chat.GetConversationAsync(conversationId), "Chat");

        /// <summary>
        /// Messages oldest first: the newest page, or with "before" (a message id)
        /// the page before that message. "limit" is 1-100, 30 when left out.
        /// </summary>
        [HttpGet("{conversationId:guid}/messages")]
        public async Task<IActionResult> GetMessages(Guid conversationId, [FromQuery] Guid? before, [FromQuery] int? limit)
            => OkOrNotFound(await _chat.GetMessagesAsync(conversationId, before, limit), "Chat");

        [HttpPost("{conversationId:guid}/messages")]
        [EnableRateLimiting("chat")]
        public async Task<IActionResult> Send(Guid conversationId, [FromBody] SendMessageDto dto)
        {
            var message = await _chat.SendAsync(conversationId, dto);
            return message == null
                ? OkOrNotFound<ChatMessageDto>(null, "Chat")
                : CreatedAtAction(nameof(GetMessages), new { conversationId }, message);
        }

        /// <summary>Marks the whole chat as read by me; the friend sees "Seen".</summary>
        [HttpPut("{conversationId:guid}/read")]
        public async Task<IActionResult> MarkRead(Guid conversationId)
            => NoContentOrNotFound(await _chat.MarkReadAsync(conversationId), "Chat");

        [HttpPut("messages/{messageId:guid}")]
        public async Task<IActionResult> Edit(Guid messageId, [FromBody] EditMessageDto dto)
            => OkOrNotFound(await _chat.EditAsync(messageId, dto), "Message");

        /// <summary>Deletes my message for everyone; it stays in the chat as "message deleted".</summary>
        [HttpDelete("messages/{messageId:guid}")]
        public async Task<IActionResult> Delete(Guid messageId)
            => NoContentOrNotFound(await _chat.DeleteAsync(messageId), "Message");

        // ── Groups ────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates a group chat: a name (1-60 characters) and at least two of
        /// my friends. I become its admin. A group holds at most 50 people.
        /// </summary>
        [HttpPost("groups")]
        public async Task<IActionResult> CreateGroup([FromBody] CreateGroupDto dto)
        {
            var group = await _groups.CreateAsync(dto);
            return CreatedAtAction(nameof(GetConversation), new { conversationId = group.Id }, group);
        }

        /// <summary>Renames a group (admins only).</summary>
        [HttpPut("{conversationId:guid}/name")]
        public async Task<IActionResult> RenameGroup(Guid conversationId, [FromBody] RenameGroupDto dto)
            => Ok(await _groups.RenameAsync(conversationId, dto));

        /// <summary>Adds friends of mine to a group I am in. People who are already in it are skipped.</summary>
        [HttpPost("{conversationId:guid}/members")]
        public async Task<IActionResult> AddMembers(Guid conversationId, [FromBody] AddMembersDto dto)
            => Ok(await _groups.AddMembersAsync(conversationId, dto));

        /// <summary>Removes someone from a group (admins only). To go myself, I leave.</summary>
        [HttpDelete("{conversationId:guid}/members/{userId:guid}")]
        public async Task<IActionResult> RemoveMember(Guid conversationId, Guid userId)
            => Ok(await _groups.RemoveMemberAsync(conversationId, userId));

        /// <summary>Makes a member of a group an admin too (admins only).</summary>
        [HttpPut("{conversationId:guid}/members/{userId:guid}/admin")]
        public async Task<IActionResult> MakeAdmin(Guid conversationId, Guid userId)
            => Ok(await _groups.MakeAdminAsync(conversationId, userId));

        /// <summary>
        /// Leaves a group. If I was its last admin, the member who has been in
        /// it longest becomes admin; if I was its last member, it is deleted.
        /// </summary>
        [HttpPost("{conversationId:guid}/leave")]
        public async Task<IActionResult> LeaveGroup(Guid conversationId)
        {
            await _groups.LeaveAsync(conversationId);
            return NoContent();
        }
    }
}
