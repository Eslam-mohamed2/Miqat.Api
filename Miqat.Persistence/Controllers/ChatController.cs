using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;

namespace Miqat.API.Controllers
{
    /// <summary>
    /// Chats between friends. Live updates arrive on the hub (/hubs/miqat):
    /// "chatMessage", "chatMessageChanged", "chatRead", "chatTyping" and
    /// "presenceChanged". While typing, the app calls the hub method
    /// "Typing" with the chat id.
    /// </summary>
    public class ChatController : BaseApiController
    {
        private readonly IChatService _chat;

        public ChatController(IChatService chat) => _chat = chat;

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
    }
}
