using System.Linq.Expressions;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Application.Specifications.Chat;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Services
{
    /// <inheritdoc />
    /// <remarks>
    /// Rules: only friends can start a chat or send to one; everyone in a chat
    /// can read its history (also after unfriending, when sending stops); only
    /// the sender edits or deletes a message. Missing answers 404, not being in
    /// the chat 403.
    /// </remarks>
    public class ChatService : IChatService
    {
        public const int DefaultPageSize = 30;
        public const int MaxPageSize = 100;
        private const int MaxClientIdLength = 64;

        /// <summary>The live events of the chat, in one place for the server and the app.</summary>
        public static class Events
        {
            public const string Message = "chatMessage";
            public const string MessageChanged = "chatMessageChanged";
            public const string Read = "chatRead";
            public const string Typing = "chatTyping";
            public const string Presence = "presenceChanged";
        }

        /// <summary>One shape for a message wherever it is read: in a page, in the chat list, live.</summary>
        private static readonly Expression<Func<ChatMessage, ChatMessageDto>> AsDto = m => new ChatMessageDto
        {
            Id = m.Id,
            ConversationId = m.ConversationId,
            SenderId = m.SenderId,
            Body = m.Body,
            CreatedAt = m.CreatedAt,
            EditedAt = m.EditedAt,
            IsDeleted = m.IsDeleted,
            CallOutcome = m.CallOutcome,
            CallVideo = m.CallVideo,
            CallSeconds = m.CallSeconds
        };

        private static readonly Func<ChatMessage, ChatMessageDto> ToDto = AsDto.Compile();

        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly PeopleDirectory _people;
        private readonly IPresenceTracker _presence;
        private readonly IRealtimeNotifier _realtime;

        public ChatService(
            IUnitOfWork unitOfWork,
            ICurrentUserService currentUser,
            PeopleDirectory people,
            IPresenceTracker presence,
            IRealtimeNotifier realtime)
        {
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _people = people;
            _presence = presence;
            _realtime = realtime;
        }

        private IGenericRepository<Conversation> Conversations => _unitOfWork.Repository<Conversation>();
        private IGenericRepository<ConversationParticipant> Participants => _unitOfWork.Repository<ConversationParticipant>();
        private IGenericRepository<ChatMessage> Messages => _unitOfWork.Repository<ChatMessage>();

        // ── Chats ─────────────────────────────────────────────────────────────

        public async Task<List<ConversationDto>> GetConversationsAsync()
        {
            var me = _currentUser.RequireUserId();
            var chats = await LoadAsync(me, conversationId: null);

            return chats.OrderByDescending(c => c.LastMessageAt).ToList();
        }

        public async Task<ConversationDto?> GetConversationAsync(Guid conversationId)
        {
            var me = _currentUser.RequireUserId();
            if (await RequireChatAsync(conversationId, me) == null) return null;

            return (await LoadAsync(me, conversationId)).Single();
        }

        public async Task<ConversationDto> OpenWithAsync(Guid friendId)
        {
            var me = _currentUser.RequireUserId();
            if (friendId == me)
                throw new ApiException("You cannot start a chat with yourself.", 400);
            if (!(await _people.GetFriendIdsAsync(me)).Contains(friendId))
                throw new ApiException("You can only chat with your friends.", 403);

            var key = Conversation.DirectKeyFor(me, friendId);
            var conversationId = (await Conversations.SelectAsync(c => c.DirectKey == key, c => (Guid?)c.Id)).FirstOrDefault();

            if (conversationId == null)
            {
                var conversation = Conversation.Direct(me, friendId);
                await Conversations.AddAsync(conversation);
                await Participants.AddAsync(new ConversationParticipant(conversation.Id, me));
                await Participants.AddAsync(new ConversationParticipant(conversation.Id, friendId));
                await _unitOfWork.CompleteAsync();

                conversationId = conversation.Id;
            }

            return (await LoadAsync(me, conversationId)).Single();
        }

        // ── Messages ──────────────────────────────────────────────────────────

        public async Task<ChatMessagePageDto?> GetMessagesAsync(Guid conversationId, Guid? beforeMessageId, int? limit)
        {
            var me = _currentUser.RequireUserId();
            if (await RequireChatAsync(conversationId, me) == null) return null;

            DateTime? before = null;
            if (beforeMessageId.HasValue)
            {
                before = (await Messages.SelectAsync(
                    m => m.Id == beforeMessageId && m.ConversationId == conversationId,
                    m => (DateTime?)m.CreatedAt)).FirstOrDefault()
                    ?? throw new ApiException("That message is not in this chat.", 400);
            }

            var size = Math.Clamp(limit ?? DefaultPageSize, 1, MaxPageSize);
            var newestFirst = await Messages.ListAsync(new ChatMessagesPageSpec(conversationId, before, size + 1));

            return new ChatMessagePageDto
            {
                HasMore = newestFirst.Count > size,
                Messages = newestFirst.Take(size).Reverse().Select(ToDto).ToList()
            };
        }

        public async Task<ChatMessageDto?> SendAsync(Guid conversationId, SendMessageDto dto)
        {
            var me = _currentUser.RequireUserId();
            var chat = await RequireChatAsync(conversationId, me);
            if (chat == null) return null;

            var friends = await _people.GetFriendIdsAsync(me);
            if (chat.Others.Any(id => !friends.Contains(id)))
                throw new ApiException("You can only send messages to your friends.", 403);

            var clientId = dto.ClientId?.Trim();
            if (clientId?.Length > MaxClientIdLength)
                throw new ApiException($"clientId cannot be longer than {MaxClientIdLength} characters.", 400);

            var message = new ChatMessage(conversationId, me, dto.Body);
            await Messages.AddAsync(message);

            // Sending a message means having read everything before it.
            (await Conversations.GetByIdAsync(conversationId))!.MessageSent(message.CreatedAt);
            chat.Mine.ReadUpTo(message.CreatedAt);
            await _unitOfWork.CompleteAsync();

            var sent = ToDto(message);
            sent.ClientId = string.IsNullOrEmpty(clientId) ? null : clientId;

            // Everyone in the chat, the sender too: their other open tabs show it at once.
            await _realtime.NotifyUsersAsync(chat.Everyone, Events.Message, sent);
            return sent;
        }

        public async Task<ChatMessageDto?> EditAsync(Guid messageId, EditMessageDto dto)
        {
            var message = await RequireOwnMessageAsync(messageId, "edit");
            if (message == null) return null;

            if (message.IsDeleted)
                throw new ApiException("A deleted message cannot be edited.", 400);

            message.Edit(dto.Body);
            return await SaveChangeAsync(message);
        }

        public async Task<bool> DeleteAsync(Guid messageId)
        {
            var message = await RequireOwnMessageAsync(messageId, "delete");
            if (message == null) return false;

            if (!message.IsDeleted)
            {
                message.Remove();
                await SaveChangeAsync(message);
            }
            return true;
        }

        // ── Read state, badge, presence ───────────────────────────────────────

        public async Task<bool> MarkReadAsync(Guid conversationId)
        {
            var me = _currentUser.RequireUserId();
            var chat = await RequireChatAsync(conversationId, me);
            if (chat == null) return false;

            var now = DateTime.UtcNow;
            if (chat.Mine.ReadUpTo(now))
            {
                await _unitOfWork.CompleteAsync();
                await _realtime.NotifyUsersAsync(chat.Everyone, Events.Read,
                    new { conversationId, userId = me, readAt = now });
            }
            return true;
        }

        public async Task<ChatUnreadDto> GetUnreadAsync()
        {
            var me = _currentUser.RequireUserId();
            var perChat = await Messages.CountGroupedAsync(
                m => m.SenderId != me && !m.IsDeleted && !m.Sender.IsDeleted &&
                     m.Conversation.Participants.Any(p => p.UserId == me && (p.LastReadAt == null || m.CreatedAt > p.LastReadAt)),
                m => m.ConversationId);

            return new ChatUnreadDto { Conversations = perChat.Count, Messages = perChat.Values.Sum() };
        }

        public async Task<List<Guid>> GetOnlineFriendIdsAsync()
        {
            var me = _currentUser.RequireUserId();
            return (await _people.GetFriendIdsAsync(me)).Where(_presence.IsOnline).ToList();
        }

        public async Task<List<Guid>> GetOthersInAsync(Guid conversationId, Guid userId)
        {
            var people = await GetParticipantsAsync(conversationId);
            return people.Contains(userId) ? people.Where(id => id != userId).ToList() : new List<Guid>();
        }

        public async Task<List<Guid>> GetParticipantsAsync(Guid conversationId) =>
            await Participants.SelectAsync(p => p.ConversationId == conversationId, p => p.UserId);

        public async Task<ChatMessageDto> LogCallAsync(Guid conversationId, Guid callerId, bool video, CallOutcome outcome, int seconds)
        {
            var message = ChatMessage.ForCall(conversationId, callerId, video, outcome, seconds);
            await Messages.AddAsync(message);
            (await Conversations.GetByIdAsync(conversationId))!.MessageSent(message.CreatedAt);

            // The caller has seen their own call; so has the friend if they
            // took it. A missed or declined call stays unread for the friend.
            var people = (await Participants.FindAsync(p => p.ConversationId == conversationId)).ToList();
            foreach (var person in people.Where(p => p.UserId == callerId || outcome == CallOutcome.Completed))
                person.ReadUpTo(message.CreatedAt);

            await _unitOfWork.CompleteAsync();

            var logged = ToDto(message);
            await _realtime.NotifyUsersAsync(people.Select(p => p.UserId), Events.Message, logged);
            return logged;
        }

        // ── helpers ───────────────────────────────────────────────────────────

        /// <summary>The caller's place in a chat and who else is in it. Null when the chat does not exist.</summary>
        private async Task<ChatAccess?> RequireChatAsync(Guid conversationId, Guid me)
        {
            var people = (await Participants.FindAsync(p => p.ConversationId == conversationId)).ToList();
            if (people.Count == 0) return null;

            var mine = people.FirstOrDefault(p => p.UserId == me)
                ?? throw new ApiException("You are not in this chat.", 403);

            return new ChatAccess(mine, people.Select(p => p.UserId).ToList());
        }

        private async Task<ChatMessage?> RequireOwnMessageAsync(Guid messageId, string action)
        {
            var message = await Messages.GetByIdAsync(messageId);
            if (message == null) return null;

            if (message.SenderId != _currentUser.RequireUserId())
                throw new ApiException($"You can only {action} your own messages.", 403);
            if (message.IsCall)
                throw new ApiException("The record of a call cannot be changed.", 400);

            return message;
        }

        /// <summary>Saves an edited or deleted message and shows the change to everyone in its chat.</summary>
        private async Task<ChatMessageDto> SaveChangeAsync(ChatMessage message)
        {
            await _unitOfWork.CompleteAsync();

            var changed = ToDto(message);
            var everyone = await Participants.SelectAsync(p => p.ConversationId == message.ConversationId, p => p.UserId);
            await _realtime.NotifyUsersAsync(everyone, Events.MessageChanged, changed);
            return changed;
        }

        /// <summary>
        /// The caller's chats (or one of them) as the list shows them, in a
        /// single query: the friend, the newest message and the unread count.
        /// </summary>
        private async Task<List<ConversationDto>> LoadAsync(Guid me, Guid? conversationId)
        {
            var rows = await Participants.SelectAsync(
                p => p.UserId == me &&
                     (conversationId == null
                         // The list: chats that have messages, with people who still have an account.
                         ? p.Conversation.LastMessageAt != null &&
                           p.Conversation.Participants.Any(o => o.UserId != me && !o.User.IsDeleted)
                         : p.ConversationId == conversationId),
                p => new ConversationRow
                {
                    Id = p.ConversationId,
                    LastMessageAt = p.Conversation.LastMessageAt,
                    Friend = p.Conversation.Participants
                        .Where(o => o.UserId != me)
                        .Select(o => new FriendRow
                        {
                            Id = o.UserId,
                            FullName = o.User.FullName,
                            ProfilePictureUrl = o.User.ProfilePictureUrl,
                            LastReadAt = o.LastReadAt
                        })
                        .FirstOrDefault(),
                    LastMessage = p.Conversation.Messages.AsQueryable()
                        .OrderByDescending(m => m.CreatedAt)
                        .Select(AsDto)
                        .FirstOrDefault(),
                    UnreadCount = p.Conversation.Messages.Count(m =>
                        m.SenderId != me && !m.IsDeleted && (p.LastReadAt == null || m.CreatedAt > p.LastReadAt))
                });

            if (rows.Count == 0) return new List<ConversationDto>();

            var friends = await _people.GetFriendIdsAsync(me);

            return rows.Select(row => new ConversationDto
            {
                Id = row.Id,
                Friend = new ChatPersonDto
                {
                    Id = row.Friend?.Id ?? Guid.Empty,
                    FullName = row.Friend?.FullName ?? "Deleted account",
                    ProfilePictureUrl = row.Friend?.ProfilePictureUrl,
                    IsOnline = row.Friend != null && _presence.IsOnline(row.Friend.Id)
                },
                LastMessage = row.LastMessage,
                LastMessageAt = row.LastMessageAt,
                UnreadCount = row.UnreadCount,
                FriendLastReadAt = row.Friend?.LastReadAt,
                CanSend = row.Friend != null && friends.Contains(row.Friend.Id)
            }).ToList();
        }

        private sealed record ChatAccess(ConversationParticipant Mine, List<Guid> Everyone)
        {
            public IEnumerable<Guid> Others => Everyone.Where(id => id != Mine.UserId);
        }

        private sealed class ConversationRow
        {
            public Guid Id { get; init; }
            public DateTime? LastMessageAt { get; init; }
            public FriendRow? Friend { get; init; }
            public ChatMessageDto? LastMessage { get; init; }
            public int UnreadCount { get; init; }
        }

        private sealed class FriendRow
        {
            public Guid Id { get; init; }
            public string FullName { get; init; } = string.Empty;
            public string? ProfilePictureUrl { get; init; }
            public DateTime? LastReadAt { get; init; }
        }
    }
}
