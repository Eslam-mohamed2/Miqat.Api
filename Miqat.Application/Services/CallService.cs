using System.Security.Cryptography;
using System.Text;
using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Application.Services
{
    /// <summary>Settings of voice and video calls ("Calls" in configuration).</summary>
    public class CallSettings
    {
        /// <summary>How long a call rings before the caller's app gives up.</summary>
        public int RingSeconds { get; set; } = 35;

        /// <summary>
        /// Servers every browser may use to find a path to the other one
        /// (STUN, or TURN with fixed credentials).
        /// </summary>
        public List<IceServerDto> IceServers { get; set; } = new();

        /// <summary>A relay (TURN) server that hands out short-lived passwords: see <see cref="TurnSettings"/>.</summary>
        public TurnSettings Turn { get; set; } = new();

        /// <summary>A hosted relay service that is asked for servers and passwords: see <see cref="RelaySettings"/>.</summary>
        public RelaySettings Relay { get; set; } = new();
    }

    /// <summary>
    /// A hosted relay (TURN) service, the kind with a free plan and nothing to
    /// run yourself. The server asks it for the relay servers and short-lived
    /// passwords and passes them on to the browsers, so the key of the
    /// service never reaches a browser.
    /// </summary>
    public class RelaySettings
    {
        /// <summary>The address that answers with the servers (the "ICE servers" address of the service).</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// The key of the service, when it is sent in the Authorization header
        /// (Cloudflare). Leave empty when the key is part of the address.
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>How long the passwords handed out should work. A day covers any call.</summary>
        public int TtlSeconds { get; set; } = 86400;
    }

    /// <summary>
    /// A TURN server set up with a shared secret (coturn: use-auth-secret).
    /// Each user gets a password that only works for a while, so the secret
    /// itself never leaves the server.
    /// </summary>
    public class TurnSettings
    {
        public List<string> Urls { get; set; } = new();
        public string Secret { get; set; } = string.Empty;
        public int TtlSeconds { get; set; } = 3600;
    }

    public sealed record CallInvite(CallSession Call, ChatPersonDto From);

    /// <summary>A call that just ended, and why, in the word the apps are told.</summary>
    public sealed record CallEnded(CallSession Call, string Reason);

    /// <inheritdoc />
    /// <remarks>
    /// The server only introduces the two browsers to each other and keeps
    /// the rules: friends only, one call at a time per person, and only the
    /// two tabs of a call may talk to each other through it.
    /// </remarks>
    public class CallService : ICallService
    {
        /// <summary>The live events of a call, in one place for the server and the app.</summary>
        public static class Events
        {
            public const string Incoming = "callIncoming";
            public const string Accepted = "callAccepted";
            public const string Signal = "callSignal";
            public const string Ended = "callEnded";
        }

        private readonly IUnitOfWork _unitOfWork;
        private readonly IChatService _chat;
        private readonly PeopleDirectory _people;
        private readonly IPresenceTracker _presence;
        private readonly CallRegistry _registry;
        private readonly CallSettings _settings;
        private readonly IRelayAccess _relay;

        public CallService(
            IUnitOfWork unitOfWork,
            IChatService chat,
            PeopleDirectory people,
            IPresenceTracker presence,
            CallRegistry registry,
            CallSettings settings,
            IRelayAccess relay)
        {
            _unitOfWork = unitOfWork;
            _chat = chat;
            _people = people;
            _presence = presence;
            _registry = registry;
            _settings = settings;
            _relay = relay;
        }

        public async Task<CallSetupDto> GetSetupAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var servers = _settings.IceServers.Where(s => s.Urls.Count > 0).ToList();

            var turn = _settings.Turn;
            if (turn.Urls.Count > 0 && !string.IsNullOrWhiteSpace(turn.Secret))
                servers.Add(TemporaryTurnAccess(turn, userId, DateTime.UtcNow));

            // Empty when no relay service is set up, or it is not answering.
            servers.AddRange(await _relay.GetServersAsync(cancellationToken));

            return new CallSetupDto { IceServers = servers, RingSeconds = _settings.RingSeconds };
        }

        /// <summary>
        /// The time-limited credentials coturn accepts: the user name is
        /// "expiry:user", the password its HMAC-SHA1 with the shared secret.
        /// </summary>
        public static IceServerDto TemporaryTurnAccess(TurnSettings turn, Guid userId, DateTime now)
        {
            var expires = new DateTimeOffset(now.AddSeconds(turn.TtlSeconds)).ToUnixTimeSeconds();
            var username = $"{expires}:{userId:N}";
            using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(turn.Secret));

            return new IceServerDto
            {
                Urls = turn.Urls.ToList(),
                Username = username,
                Credential = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(username)))
            };
        }

        public async Task<CallInvite> StartAsync(Guid callerId, string connectionId, Guid conversationId, bool video)
        {
            var people = await _chat.GetParticipantsAsync(conversationId);
            if (people.Count == 0)
                throw new ApiException("Chat not found.", 404);
            if (!people.Contains(callerId))
                throw new ApiException("You are not in this chat.", 403);

            var calleeId = people.First(id => id != callerId);
            if (!(await _people.GetFriendIdsAsync(callerId)).Contains(calleeId))
                throw new ApiException("You can only call your friends.", 403);

            if (!_registry.TryCountAttempt(callerId, DateTime.UtcNow))
                throw new ApiException("You are calling too often. Wait a minute and try again.", 429);

            var names = await _unitOfWork.Repository<User>().SelectAsync(
                u => u.Id == callerId || u.Id == calleeId,
                u => new ChatPersonDto { Id = u.Id, FullName = u.FullName, ProfilePictureUrl = u.ProfilePictureUrl });
            var caller = names.First(p => p.Id == callerId);
            var calleeName = names.FirstOrDefault(p => p.Id == calleeId)?.FullName ?? "Your friend";

            var call = new CallSession
            {
                ConversationId = conversationId,
                CallerId = callerId,
                CalleeId = calleeId,
                Video = video,
                CallerConnectionId = connectionId,
                StartedAt = DateTime.UtcNow
            };

            // Nobody to ring: the friend finds a missed call in the chat instead.
            if (!_presence.IsOnline(calleeId))
            {
                await _chat.LogCallAsync(conversationId, callerId, video, CallOutcome.Missed, 0);
                throw new ApiException($"{calleeName} is not online right now. They will see a missed call.", 409);
            }

            if (!_registry.TryStart(call, out var busyUserId))
            {
                if (busyUserId == callerId)
                    throw new ApiException("You are already on a call.", 409);

                await _chat.LogCallAsync(conversationId, callerId, video, CallOutcome.Missed, 0);
                throw new ApiException($"{calleeName} is on another call. They will see a missed call.", 409);
            }

            caller.IsOnline = true;
            return new CallInvite(call, caller);
        }

        public CallSession Answer(Guid callId, Guid userId, string connectionId)
        {
            var call = _registry.TryAnswer(callId, userId, connectionId, DateTime.UtcNow) ? _registry.Get(callId) : null;
            return call ?? throw new ApiException("That call is no longer ringing.", 409);
        }

        public string? PeerConnectionOf(Guid callId, string connectionId) =>
            _registry.Get(callId)?.PeerOf(connectionId);

        public Task<CallEnded?> EndAsync(Guid callId, Guid userId)
        {
            var call = _registry.Get(callId);
            return call == null || !call.Includes(userId)
                ? Task.FromResult<CallEnded?>(null)
                : FinishAsync(call, endedBy: userId);
        }

        public Task<CallEnded?> EndForConnectionAsync(string connectionId)
        {
            var call = _registry.FindByConnection(connectionId);
            return call == null
                ? Task.FromResult<CallEnded?>(null)
                : FinishAsync(call, endedBy: connectionId == call.CallerConnectionId ? call.CallerId : call.CalleeId);
        }

        /// <summary>Ends a call once (a second "end" for the same call finds nothing) and writes it into the chat.</summary>
        private async Task<CallEnded?> FinishAsync(CallSession call, Guid endedBy)
        {
            if (_registry.Remove(call.Id) == null) return null;

            var (outcome, reason, seconds) =
                call.IsAnswered ? (CallOutcome.Completed, "ended", (int)(DateTime.UtcNow - call.AnsweredAt!.Value).TotalSeconds)
                : endedBy == call.CalleeId ? (CallOutcome.Declined, "declined", 0)
                : (CallOutcome.Missed, "cancelled", 0);

            await _chat.LogCallAsync(call.ConversationId, call.CallerId, call.Video, outcome, seconds);
            return new CallEnded(call, reason);
        }
    }
}
