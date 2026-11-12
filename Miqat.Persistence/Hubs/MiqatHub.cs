using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using System.Text.Json;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Services;

namespace Miqat.API.Hubs
{
    /// <summary>
    /// The app's single realtime channel. Each connection is joined to a
    /// per-user group on connect, so the server can address "every tab this
    /// person has open" without tracking connection ids itself — SignalR
    /// removes the connection from the group automatically on disconnect.
    /// <para>
    /// It also knows who is online (their friends are told when that changes),
    /// carries "is typing", and sets up voice and video calls: it rings the
    /// friend and passes the two browsers' set-up messages to each other. The
    /// call itself never passes through here.
    /// </para>
    /// </summary>
    [Authorize]
    public class MiqatHub : Hub
    {
        private readonly IPresenceTracker _presence;
        private readonly PeopleDirectory _people;
        private readonly IChatService _chat;
        private readonly ICallService _calls;
        private readonly ILogger<MiqatHub> _logger;

        public MiqatHub(
            IPresenceTracker presence, PeopleDirectory people, IChatService chat, ICallService calls, ILogger<MiqatHub> logger)
        {
            _logger = logger;
            _presence = presence;
            _people = people;
            _chat = chat;
            _calls = calls;
        }

        public static string UserGroup(string userId) => $"user:{userId}";
        private static string UserGroup(Guid userId) => UserGroup(userId.ToString());

        public override async Task OnConnectedAsync()
        {
            if (CurrentUserId is Guid userId)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
                if (_presence.Connected(userId)) await TellFriendsAsync(userId, isOnline: true);
            }

            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            // A call does not outlive the tab it was made in. Whatever goes wrong
            // while ending it, the person must still be marked offline below.
            try
            {
                await AnnounceEndAsync(await _calls.EndForConnectionAsync(Context.ConnectionId));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ending the call of a closed connection failed");
            }

            if (CurrentUserId is Guid userId && _presence.Disconnected(userId))
                await TellFriendsAsync(userId, isOnline: false);

            await base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Called by the app while the user types in a chat (at most every few
        /// seconds). The others in the chat get "chatTyping"; nobody else does.
        /// </summary>
        public async Task Typing(Guid conversationId)
        {
            if (CurrentUserId is not Guid userId) return;

            var others = await _chat.GetOthersInAsync(conversationId, userId);
            if (others.Count == 0) return;

            await Clients.Groups(others.Select(UserGroup).ToList())
                .SendAsync(ChatService.Events.Typing, new { conversationId, userId });
        }

        // ── Calls ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Rings the friend of a chat ("callIncoming" in every tab they have
        /// open) and answers the id of the new call. Fails with a message to
        /// show when the friend is offline or busy.
        /// </summary>
        public async Task<Guid> StartCall(Guid conversationId, bool video)
        {
            var invite = await GuardedAsync(() => _calls.StartAsync(RequireUserId(), Context.ConnectionId, conversationId, video));
            var call = invite.Call;

            await Clients.Group(UserGroup(call.CalleeId)).SendAsync(CallService.Events.Incoming,
                new { callId = call.Id, call.ConversationId, call.Video, from = invite.From });
            return call.Id;
        }

        /// <summary>The friend picks up (in this tab) or says no.</summary>
        public async Task AnswerCall(Guid callId, bool accept)
        {
            if (!accept)
            {
                await EndCall(callId);
                return;
            }

            var call = await GuardedAsync(() => Task.FromResult(_calls.Answer(callId, RequireUserId(), Context.ConnectionId)));

            await Clients.Client(call.CallerConnectionId).SendAsync(CallService.Events.Accepted, new { callId });
            // It stops ringing in the friend's other tabs.
            await Clients.GroupExcept(UserGroup(call.CalleeId), Context.ConnectionId)
                .SendAsync(CallService.Events.Ended, new { callId, reason = "answeredElsewhere" });
        }

        /// <summary>
        /// Passes a set-up message (offer, answer, network candidate) to the
        /// other tab of an answered call. From anyone else it goes nowhere.
        /// </summary>
        public async Task SendCallSignal(Guid callId, JsonElement data)
        {
            var peer = _calls.PeerConnectionOf(callId, Context.ConnectionId);
            if (peer == null) return;

            await Clients.Client(peer).SendAsync(CallService.Events.Signal, new { callId, data });
        }

        /// <summary>Hang up, or stop ringing. Both sides get "callEnded", and the call appears in their chat.</summary>
        public async Task EndCall(Guid callId) =>
            await AnnounceEndAsync(await _calls.EndAsync(callId, RequireUserId()));

        private async Task AnnounceEndAsync(CallEnded? ended)
        {
            if (ended == null) return;

            // Every tab of both: the ones that were ringing or showing the call close it.
            await Clients.Groups(UserGroup(ended.Call.CallerId), UserGroup(ended.Call.CalleeId))
                .SendAsync(CallService.Events.Ended, new { callId = ended.Call.Id, reason = ended.Reason });
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private Guid? CurrentUserId =>
            Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

        private Guid RequireUserId() => CurrentUserId ?? throw new HubException("Sign in first.");

        /// <summary>A rule that said no reaches the app as a message it can show, not as a generic failure.</summary>
        private static async Task<T> GuardedAsync<T>(Func<Task<T>> action)
        {
            try
            {
                return await action();
            }
            catch (ApiException ex)
            {
                throw new HubException(ex.Message);
            }
        }

        private async Task TellFriendsAsync(Guid userId, bool isOnline)
        {
            var friends = await _people.GetFriendIdsAsync(userId);
            if (friends.Count == 0) return;

            await Clients.Groups(friends.Select(UserGroup).ToList())
                .SendAsync(ChatService.Events.Presence, new { userId, isOnline });
        }
    }
}
