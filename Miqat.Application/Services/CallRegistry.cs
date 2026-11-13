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

    /// <summary>One person in a group call, and the browser tab they are in it with.</summary>
    public sealed record GroupCallSeat(Guid UserId, string ConnectionId, DateTime JoinedAt);

    /// <summary>
    /// A group call as it is at one moment. A copy: what the registry holds
    /// changes under its lock only.
    /// </summary>
    public sealed record GroupCall(
        Guid Id, Guid ConversationId, Guid StartedByUserId, bool Video, DateTime StartedAt, IReadOnlyList<GroupCallSeat> Seats)
    {
        /// <summary>How many people can be in one call: every pair of browsers is connected directly.</summary>
        public const int MaxPeople = 8;

        public bool Includes(Guid userId) => Seats.Any(s => s.UserId == userId);
    }

    public enum GroupCallEntry
    {
        /// <summary>The group had no call: this person started one and is alone in it.</summary>
        Started,
        Joined,
        /// <summary>The person is on another call (or in this one, in another tab).</summary>
        Busy,
        Full,
        /// <summary>The call is over.</summary>
        Gone
    }

    public sealed record GroupCallEntered(GroupCallEntry Entry, GroupCall? Call);

    /// <summary>
    /// Someone left a group call. <paramref name="Ended"/>: they were the last,
    /// and the call is over; <paramref name="Seconds"/> then counts from the
    /// moment a second person joined, and <paramref name="TookPart"/> is
    /// everyone who was ever in it.
    /// </summary>
    public sealed record GroupCallLeft(GroupCall Call, Guid UserId, bool Ended, int Seconds, IReadOnlyCollection<Guid> TookPart);

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
        // Group calls by chat: a group has one call at a time. Read and changed under the lock only.
        private readonly Dictionary<Guid, Room> _rooms = new();
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

        /// <summary>On a call between two (ringing or answered) or in a group call.</summary>
        public bool IsBusy(Guid userId, DateTime now)
        {
            lock (_gate) return IsBusyLocked(userId, now);
        }

        /// <summary>Registers a new call, unless one of the two is already on one.</summary>
        public bool TryStart(CallSession call, out Guid? busyUserId)
        {
            lock (_gate)
            {
                foreach (var stale in _calls.Values.Where(c => IsRungOut(c, call.StartedAt)).ToList())
                    _calls.TryRemove(stale.Id, out _);

                busyUserId = new[] { call.CallerId, call.CalleeId }
                    .Where(id => IsBusyLocked(id, call.StartedAt))
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

        private bool IsBusyLocked(Guid userId, DateTime now) =>
            _calls.Values.Any(call => call.Includes(userId) && !IsRungOut(call, now)) ||
            _rooms.Values.Any(room => room.Seats.Any(s => s.UserId == userId));

        // ── Group calls ───────────────────────────────────────────────────────

        /// <summary>
        /// Starts the call of a group chat with this person in it, or puts
        /// them into the one that is already going on there.
        /// </summary>
        public GroupCallEntered StartOrJoinGroup(Guid conversationId, Guid userId, string connectionId, bool video, DateTime now)
        {
            lock (_gate)
            {
                if (_rooms.TryGetValue(conversationId, out var existing))
                    return Enter(existing, userId, connectionId, now);

                if (IsBusyLocked(userId, now)) return new GroupCallEntered(GroupCallEntry.Busy, null);

                var room = new Room(conversationId, userId, video, now);
                room.Seat(userId, connectionId, now);
                _rooms[conversationId] = room;
                return new GroupCallEntered(GroupCallEntry.Started, room.Copy());
            }
        }

        public GroupCallEntered JoinGroup(Guid callId, Guid userId, string connectionId, DateTime now)
        {
            lock (_gate)
            {
                var room = _rooms.Values.FirstOrDefault(r => r.Id == callId);
                return room == null ? new GroupCallEntered(GroupCallEntry.Gone, null) : Enter(room, userId, connectionId, now);
            }
        }

        /// <summary>Takes a person out of a group call. Null when they are not in it.</summary>
        public GroupCallLeft? LeaveGroup(Guid callId, Guid userId, DateTime now)
        {
            lock (_gate)
            {
                var room = _rooms.Values.FirstOrDefault(r => r.Id == callId);
                return room == null ? null : Leave(room, s => s.UserId == userId, now);
            }
        }

        /// <summary>A tab closed: the person who was in a group call with it leaves that call.</summary>
        public GroupCallLeft? LeaveGroupByConnection(string connectionId, DateTime now)
        {
            lock (_gate)
            {
                var room = _rooms.Values.FirstOrDefault(r => r.Seats.Any(s => s.ConnectionId == connectionId));
                return room == null ? null : Leave(room, s => s.ConnectionId == connectionId, now);
            }
        }

        /// <summary>A person is no longer in a group chat: they leave its call too.</summary>
        public GroupCallLeft? LeaveGroupOf(Guid conversationId, Guid userId, DateTime now)
        {
            lock (_gate)
            {
                return _rooms.TryGetValue(conversationId, out var room) ? Leave(room, s => s.UserId == userId, now) : null;
            }
        }

        public GroupCall? GetGroup(Guid callId)
        {
            lock (_gate) return _rooms.Values.FirstOrDefault(r => r.Id == callId)?.Copy();
        }

        /// <summary>The calls going on in these group chats, by chat.</summary>
        public Dictionary<Guid, GroupCall> GetGroupsIn(IEnumerable<Guid> conversationIds)
        {
            lock (_gate)
            {
                return conversationIds.Distinct()
                    .Where(_rooms.ContainsKey)
                    .ToDictionary(id => id, id => _rooms[id].Copy());
            }
        }

        /// <summary>
        /// Where a set-up message from one person of a group call to another
        /// goes: the tab the other is in the call with. Null unless the message
        /// comes from the tab the sender is in the call with.
        /// </summary>
        public string? GroupSeatOf(Guid callId, Guid fromUserId, string fromConnectionId, Guid toUserId)
        {
            lock (_gate)
            {
                var room = _rooms.Values.FirstOrDefault(r => r.Id == callId);
                if (room == null || fromUserId == toUserId ||
                    !room.Seats.Any(s => s.UserId == fromUserId && s.ConnectionId == fromConnectionId))
                    return null;

                return room.Seats.FirstOrDefault(s => s.UserId == toUserId)?.ConnectionId;
            }
        }

        private GroupCallEntered Enter(Room room, Guid userId, string connectionId, DateTime now)
        {
            if (IsBusyLocked(userId, now)) return new GroupCallEntered(GroupCallEntry.Busy, null);
            if (room.Seats.Count >= GroupCall.MaxPeople) return new GroupCallEntered(GroupCallEntry.Full, null);

            room.Seat(userId, connectionId, now);
            return new GroupCallEntered(GroupCallEntry.Joined, room.Copy());
        }

        private GroupCallLeft? Leave(Room room, Func<GroupCallSeat, bool> who, DateTime now)
        {
            var seat = room.Seats.FirstOrDefault(who);
            if (seat == null) return null;

            room.Seats.Remove(seat);
            var ended = room.Seats.Count == 0;
            if (ended) _rooms.Remove(room.ConversationId);

            var seconds = ended && room.ConnectedAt != null ? (int)(now - room.ConnectedAt.Value).TotalSeconds : 0;
            return new GroupCallLeft(room.Copy(), seat.UserId, ended, Math.Max(0, seconds), room.TookPart.ToList());
        }

        /// <summary>A group call as the registry keeps it.</summary>
        private sealed class Room
        {
            public Room(Guid conversationId, Guid startedByUserId, bool video, DateTime startedAt)
            {
                ConversationId = conversationId;
                StartedByUserId = startedByUserId;
                Video = video;
                StartedAt = startedAt;
            }

            public Guid Id { get; } = Guid.NewGuid();
            public Guid ConversationId { get; }
            public Guid StartedByUserId { get; }
            public bool Video { get; }
            public DateTime StartedAt { get; }

            /// <summary>Who is in the call now, in the order they joined.</summary>
            public List<GroupCallSeat> Seats { get; } = new();

            /// <summary>Everyone who was in the call at some point.</summary>
            public HashSet<Guid> TookPart { get; } = new();

            /// <summary>When a second person joined: from then on it is a conversation.</summary>
            public DateTime? ConnectedAt { get; private set; }

            public void Seat(Guid userId, string connectionId, DateTime now)
            {
                Seats.Add(new GroupCallSeat(userId, connectionId, now));
                TookPart.Add(userId);
                if (TookPart.Count == 2) ConnectedAt ??= now;
            }

            public GroupCall Copy() => new(Id, ConversationId, StartedByUserId, Video, StartedAt, Seats.ToList());
        }
    }
}
