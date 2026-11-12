using Microsoft.EntityFrameworkCore;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.infrastructure.persistence.Data.Seeds
{
    /// <summary>
    /// Gives every demo account a few chats with its friends, so the chat
    /// bubbles have something to show: a short conversation each, the newest
    /// message from the friend and still unread.
    /// <para>
    /// Runs on every start-up while demo data is on, and only touches accounts
    /// that are in no chat yet - so it never adds anything twice.
    /// </para>
    /// </summary>
    public class ChatDemoSeeder
    {
        private const int MaxChatsPerAccount = 3;

        /// <summary>(from the friend?, text, minutes before now)</summary>
        private static readonly (bool FromFriend, string Text, int MinutesAgo)[][] Scripts =
        {
            new[]
            {
                (true, "Hi! Did you get a chance to look at the plan?", 2 * 24 * 60),
                (false, "Yes, it looks good. I left two comments on the timeline.", 2 * 24 * 60 - 12),
                (true, "Great, I'll update it today.", 2 * 24 * 60 - 15),
                (true, "Are we still on for the stand-up tomorrow?", 55)
            },
            new[]
            {
                (false, "Can you send me the slides from yesterday?", 26 * 60),
                (true, "Sure, they're attached to the task now.", 25 * 60),
                (false, "Got them, thanks!", 25 * 60 - 3),
                (true, "Let me know if anything is unclear.", 20)
            },
            new[]
            {
                (true, "Lunch at one?", 3 * 60),
                (false, "Works for me. The usual place?", 3 * 60 - 4),
                (true, "Yes, see you there", 5)
            }
        };

        private readonly MiqatDbContext _context;

        public ChatDemoSeeder(MiqatDbContext context) => _context = context;

        public async Task SeedAsync()
        {
            // Accounts that can sign in and are in no chat yet.
            var users = await _context.Users
                .Where(u => !u.IsDeleted && u.IsActive && u.IsVerified &&
                            u.PasswordHash != null && u.PasswordHash != "" &&
                            !_context.ConversationParticipants.Any(p => p.UserId == u.Id))
                .Select(u => u.Id)
                .ToListAsync();
            if (users.Count == 0) return;

            var friendships = await _context.Friendships
                .Where(f => f.Status == FriendshipStatus.Accepted && !f.IsDeleted &&
                            (users.Contains(f.SenderId) || users.Contains(f.ReceiverId)))
                .OrderBy(f => f.CreatedAt).ThenBy(f => f.Id)
                .Select(f => new { f.SenderId, f.ReceiverId })
                .ToListAsync();

            var existingKeys = (await _context.Conversations
                .Where(c => c.DirectKey != null)
                .Select(c => c.DirectKey!)
                .ToListAsync()).ToHashSet();

            var now = DateTime.UtcNow;

            foreach (var userId in users)
            {
                var friendIds = friendships
                    .Where(f => f.SenderId == userId || f.ReceiverId == userId)
                    .Select(f => f.SenderId == userId ? f.ReceiverId : f.SenderId)
                    .Distinct()
                    .Take(MaxChatsPerAccount)
                    .ToList();

                for (var i = 0; i < friendIds.Count; i++)
                {
                    var friendId = friendIds[i];
                    if (!existingKeys.Add(Conversation.DirectKeyFor(userId, friendId))) continue;

                    AddChat(userId, friendId, Scripts[i % Scripts.Length], now);
                }
            }

            await _context.SaveChangesAsync();
        }

        private void AddChat(Guid userId, Guid friendId, (bool FromFriend, string Text, int MinutesAgo)[] script, DateTime now)
        {
            var conversation = Conversation.Direct(userId, friendId);
            var me = new ConversationParticipant(conversation.Id, userId);
            var friend = new ConversationParticipant(conversation.Id, friendId);

            _context.Conversations.Add(conversation);
            _context.ConversationParticipants.AddRange(me, friend);

            foreach (var (fromFriend, text, minutesAgo) in script)
            {
                var sentAt = now.AddMinutes(-minutesAgo);
                var message = new ChatMessage(conversation.Id, fromFriend ? friendId : userId, text);

                // Seeded rows get their time in the past; the constructor stamps "now".
                _context.ChatMessages.Add(message);
                _context.Entry(message).Property(m => m.CreatedAt).CurrentValue = sentAt;

                conversation.MessageSent(sentAt);

                // Each side has read up to what it sent itself; the friend's
                // newest message stays unread for the demo account.
                (fromFriend ? friend : me).ReadUpTo(sentAt);
            }
        }
    }
}
