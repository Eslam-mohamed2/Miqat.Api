using System.Collections.Concurrent;

namespace Miqat.Application.Services
{
    /// <summary>
    /// A call that is ringing or going on. Calls live in the memory of the
    /// server only: the sound and picture travel between the two browsers, and
    /// what stays afterwards is the line the call leaves in the chat.
    /// </summary>
    public sealed class CallSession
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public Guid ConversationId { get; init; }
        public Guid CallerId { get; init; }
        public Guid CalleeId { get; init; }
        public bool Video { get; init; }
        public DateTime StartedAt { get; init; }

        /// <summary>The browser tab the call was started from.</summary>
        public string CallerConnectionId { get; init; } = string.Empty;

        /// <summary>The tab that answered. Null while the call rings (it rings in every tab of the friend).</summary>
        public string? CalleeConnectionId { get; set; }
        public DateTime? AnsweredAt { get; set; }

        public bool IsAnswered => AnsweredAt != null;
        public bool Includes(Guid userId) => userId == CallerId || userId == CalleeId;

        /// <summary>The other side's tab, for a message from one of the two tabs of an answered call; otherwise null.</summary>
        public string? PeerOf(string connectionId) =>
            !IsAnswered ? null
            : connectionId == CallerConnectionId ? CalleeConnectionId
            : connectionId == CalleeConnectionId ? CallerConnectionId
            : null;
    }

    /// <summary>
    /// The calls of this server, and who is busy. In memory, which is enough
    /// for one server (like the presence tracker next to it).
    /// </summary>
    public sealed class CallRegistry
    {
        /// <summary>
        /// A call that rang this long without an answer is over, even if the
        /// caller's app never said so (closed laptop, lost network).
        /// </summary>
        public static readonly TimeSpan RingLimit = TimeSpan.FromSeconds(60);

        /// <summary>How many calls one person may start per minute. More than a person needs, far less than a script would send.</summary>
        public const int MaxAttemptsPerMinute = 8;

        private readonly ConcurrentDictionary<Guid, CallSession> _calls = new();
        private readonly Dictionary<Guid, Queue<DateTime>> _attempts = new();
        private readonly object _gate = new();

        /// <summary>
        /// Counts an attempt to call. False when the person has rung too often
        /// in the last minute - every attempt rings a friend or leaves them a
        /// missed call, so it must not be free to repeat.
        /// </summary>
        public bool TryCountAttempt(Guid callerId, DateTime now)
        {
            lock (_gate)
            {
                if (!_attempts.TryGetValue(callerId, out var times))
                    _attempts[callerId] = times = new Queue<DateTime>();

                while (times.Count > 0 && now - times.Peek() >= TimeSpan.FromMinutes(1)) times.Dequeue();
                if (times.Count >= MaxAttemptsPerMinute) return false;

                times.Enqueue(now);
                return true;
            }
        }

        public CallSession? Get(Guid callId) => _calls.GetValueOrDefault(callId);

        public bool IsBusy(Guid userId, DateTime now) =>
            _calls.Values.Any(call => call.Includes(userId) && !IsRungOut(call, now));

        /// <summary>Registers a new call, unless one of the two is already on one.</summary>
        public bool TryStart(CallSession call, out Guid? busyUserId)
        {
            lock (_gate)
            {
                foreach (var stale in _calls.Values.Where(c => IsRungOut(c, call.StartedAt)).ToList())
                    _calls.TryRemove(stale.Id, out _);

                busyUserId = new[] { call.CallerId, call.CalleeId }
                    .Where(id => _calls.Values.Any(c => c.Includes(id)))
                    .Select(id => (Guid?)id)
                    .FirstOrDefault();
                if (busyUserId != null) return false;

                _calls[call.Id] = call;
                return true;
            }
        }

        /// <summary>The friend picks up in one of their tabs. False when the call is gone or already answered.</summary>
        public bool TryAnswer(Guid callId, Guid userId, string connectionId, DateTime now)
        {
            lock (_gate)
            {
                if (!_calls.TryGetValue(callId, out var call) || call.CalleeId != userId || call.IsAnswered)
                    return false;

                call.CalleeConnectionId = connectionId;
                call.AnsweredAt = now;
                return true;
            }
        }

        /// <summary>Takes a call out. Null when someone else already ended it.</summary>
        public CallSession? Remove(Guid callId) => _calls.TryRemove(callId, out var call) ? call : null;

        /// <summary>The call a tab is part of: the one it started, or the one it answered.</summary>
        public CallSession? FindByConnection(string connectionId) =>
            _calls.Values.FirstOrDefault(c => c.CallerConnectionId == connectionId || c.CalleeConnectionId == connectionId);

        private static bool IsRungOut(CallSession call, DateTime now) =>
            !call.IsAnswered && now - call.StartedAt > RingLimit;
    }
}
