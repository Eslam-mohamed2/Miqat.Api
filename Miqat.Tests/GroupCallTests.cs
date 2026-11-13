using Miqat.Application.Common;
using Miqat.Application.Modules;
using Miqat.Application.Services;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Tests;

/// <summary>Who is in the call of a group chat, who is rung, how it ends and what it leaves in the chat.</summary>
public class GroupCallTests
{
    private static readonly Guid Ann = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Ben = Guid.Parse("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Cy = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid Trip = Guid.Parse("00000000-0000-0000-0000-0000000000f1");
    private static readonly Guid Work = Guid.Parse("00000000-0000-0000-0000-0000000000f2");
    private static readonly DateTime Noon = new(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc);

    // ── The room ──────────────────────────────────────────────────────────────

    [Fact]
    public void The_first_to_call_starts_the_room_and_the_next_one_joins_it()
    {
        var registry = new CallRegistry();

        var started = registry.StartOrJoinGroup(Trip, Ann, "tab-ann", video: true, Noon);
        var joined = registry.StartOrJoinGroup(Trip, Ben, "tab-ben", video: false, Noon.AddSeconds(3));

        Assert.Equal(GroupCallEntry.Started, started.Entry);
        Assert.Equal(GroupCallEntry.Joined, joined.Entry);
        Assert.Equal(started.Call!.Id, joined.Call!.Id);
        // The call stays what its starter made it.
        Assert.True(joined.Call.Video);
        Assert.Equal(Ann, joined.Call.StartedByUserId);
        Assert.Equal(new[] { Ann, Ben }, joined.Call.Seats.Select(s => s.UserId));
        Assert.Equal(joined.Call.Seats, registry.GetGroup(started.Call.Id)!.Seats);
    }

    [Fact]
    public void A_person_is_in_one_call_at_a_time_counting_calls_between_two()
    {
        var registry = new CallRegistry();
        var trip = registry.StartOrJoinGroup(Trip, Ann, "tab-ann", false, Noon).Call!;

        // In a group call: no second group call, not this one again from another tab, and busy for a call between two.
        Assert.Equal(GroupCallEntry.Busy, registry.StartOrJoinGroup(Work, Ann, "tab-ann", false, Noon).Entry);
        Assert.Equal(GroupCallEntry.Busy, registry.JoinGroup(trip.Id, Ann, "tab-ann-2", Noon).Entry);
        Assert.True(registry.IsBusy(Ann, Noon));
        Assert.False(registry.TryStart(Between(Ben, Ann, Noon), out var busy));
        Assert.Equal(Ann, busy);
        Assert.False(registry.TryStart(Between(Ann, Ben, Noon), out busy));
        Assert.Equal(Ann, busy);

        // On a call between two (ringing counts): no group call.
        Assert.True(registry.TryStart(Between(Ben, Cy, Noon), out _));
        Assert.Equal(GroupCallEntry.Busy, registry.JoinGroup(trip.Id, Ben, "tab-ben", Noon).Entry);
        Assert.Equal(GroupCallEntry.Busy, registry.StartOrJoinGroup(Work, Cy, "tab-cy", false, Noon).Entry);

        // Once that call has rung out, both are free again.
        var later = Noon + CallRegistry.RingLimit + TimeSpan.FromSeconds(1);
        Assert.Equal(GroupCallEntry.Joined, registry.JoinGroup(trip.Id, Ben, "tab-ben", later).Entry);
    }

    [Fact]
    public void A_group_call_holds_eight_people()
    {
        var registry = new CallRegistry();
        var call = registry.StartOrJoinGroup(Trip, Guid.NewGuid(), "tab-0", false, Noon).Call!;

        for (var i = 1; i < GroupCall.MaxPeople; i++)
            Assert.Equal(GroupCallEntry.Joined, registry.JoinGroup(call.Id, Guid.NewGuid(), $"tab-{i}", Noon).Entry);

        Assert.Equal(8, registry.GetGroup(call.Id)!.Seats.Count);
        Assert.Equal(GroupCallEntry.Full, registry.JoinGroup(call.Id, Ann, "tab-ann", Noon).Entry);
        Assert.False(registry.IsBusy(Ann, Noon));

        // A place frees up when someone leaves.
        registry.LeaveGroup(call.Id, call.StartedByUserId, Noon);
        Assert.Equal(GroupCallEntry.Joined, registry.JoinGroup(call.Id, Ann, "tab-ann", Noon).Entry);
    }

    [Fact]
    public void Leaving_does_not_end_the_call_and_the_last_one_out_does()
    {
        var registry = new CallRegistry();
        var call = registry.StartOrJoinGroup(Trip, Ann, "tab-ann", false, Noon).Call!;
        registry.JoinGroup(call.Id, Ben, "tab-ben", Noon.AddSeconds(10));
        registry.JoinGroup(call.Id, Cy, "tab-cy", Noon.AddSeconds(20));

        // The starter goes first: the call goes on without them.
        var first = registry.LeaveGroup(call.Id, Ann, Noon.AddSeconds(30))!;
        Assert.False(first.Ended);
        Assert.Equal(Ann, first.UserId);
        Assert.Equal(new[] { Ben, Cy }, first.Call.Seats.Select(s => s.UserId));
        Assert.False(registry.IsBusy(Ann, Noon.AddSeconds(30)));
        Assert.NotNull(registry.GetGroup(call.Id));

        Assert.False(registry.LeaveGroup(call.Id, Ben, Noon.AddSeconds(40))!.Ended);

        var last = registry.LeaveGroup(call.Id, Cy, Noon.AddSeconds(70))!;
        Assert.True(last.Ended);
        Assert.Empty(last.Call.Seats);
        // Counted from the moment a second person joined (12:00:10) to the end (12:01:10).
        Assert.Equal(60, last.Seconds);
        Assert.Equal(new HashSet<Guid> { Ann, Ben, Cy }, last.TookPart.ToHashSet());

        // Gone: nobody can leave it twice or join it, and the group can start a new one.
        Assert.Null(registry.GetGroup(call.Id));
        Assert.Null(registry.LeaveGroup(call.Id, Cy, Noon.AddSeconds(71)));
        Assert.Equal(GroupCallEntry.Gone, registry.JoinGroup(call.Id, Ann, "tab-ann", Noon.AddSeconds(71)).Entry);
        var next = registry.StartOrJoinGroup(Trip, Ben, "tab-ben", true, Noon.AddSeconds(80));
        Assert.Equal(GroupCallEntry.Started, next.Entry);
        Assert.NotEqual(call.Id, next.Call!.Id);
    }

    [Fact]
    public void A_call_nobody_joined_has_no_length_and_one_person()
    {
        var registry = new CallRegistry();
        var call = registry.StartOrJoinGroup(Trip, Ann, "tab-ann", true, Noon).Call!;

        var left = registry.LeaveGroup(call.Id, Ann, Noon.AddSeconds(35))!;

        Assert.True(left.Ended);
        Assert.Equal(0, left.Seconds);
        Assert.Equal(new[] { Ann }, left.TookPart);
    }

    [Fact]
    public void Someone_who_comes_back_is_counted_once()
    {
        var registry = new CallRegistry();
        var call = registry.StartOrJoinGroup(Trip, Ann, "tab-ann", false, Noon).Call!;
        registry.JoinGroup(call.Id, Ben, "tab-ben", Noon.AddSeconds(5));
        registry.LeaveGroup(call.Id, Ben, Noon.AddSeconds(10));
        registry.JoinGroup(call.Id, Ben, "tab-ben-2", Noon.AddSeconds(20));
        registry.LeaveGroup(call.Id, Ben, Noon.AddSeconds(30));

        var last = registry.LeaveGroup(call.Id, Ann, Noon.AddSeconds(65))!;

        Assert.Equal(2, last.TookPart.Count);
        Assert.Equal(60, last.Seconds);
    }

    [Fact]
    public void A_closed_tab_takes_its_person_out_and_any_other_tab_does_not()
    {
        var registry = new CallRegistry();
        var call = registry.StartOrJoinGroup(Trip, Ann, "tab-ann", false, Noon).Call!;
        registry.JoinGroup(call.Id, Ben, "tab-ben", Noon);

        // Another tab of Ben, one that never joined.
        Assert.Null(registry.LeaveGroupByConnection("tab-ben-2", Noon));
        Assert.True(registry.IsBusy(Ben, Noon));

        var left = registry.LeaveGroupByConnection("tab-ben", Noon.AddSeconds(5))!;
        Assert.Equal(Ben, left.UserId);
        Assert.False(left.Ended);
        Assert.False(registry.IsBusy(Ben, Noon.AddSeconds(5)));

        Assert.True(registry.LeaveGroupByConnection("tab-ann", Noon.AddSeconds(9))!.Ended);
    }

    [Fact]
    public void Someone_taken_out_of_the_group_leaves_its_call_only()
    {
        var registry = new CallRegistry();
        registry.StartOrJoinGroup(Trip, Ann, "tab-ann", false, Noon);
        registry.StartOrJoinGroup(Trip, Ben, "tab-ben", false, Noon);

        Assert.Null(registry.LeaveGroupOf(Work, Ben, Noon));
        Assert.Null(registry.LeaveGroupOf(Trip, Cy, Noon));
        Assert.Equal(Ben, registry.LeaveGroupOf(Trip, Ben, Noon)!.UserId);
        Assert.Equal(new[] { Trip }, registry.GetGroupsIn(new[] { Trip, Work }).Keys);
    }

    [Fact]
    public void Set_up_messages_pass_between_two_people_in_the_call_from_the_tab_that_joined()
    {
        var registry = new CallRegistry();
        var call = registry.StartOrJoinGroup(Trip, Ann, "tab-ann", false, Noon).Call!;
        registry.JoinGroup(call.Id, Ben, "tab-ben", Noon);

        Assert.Equal("tab-ben", registry.GroupSeatOf(call.Id, Ann, "tab-ann", Ben));
        Assert.Equal("tab-ann", registry.GroupSeatOf(call.Id, Ben, "tab-ben", Ann));

        // Not from another tab of someone in the call, not from or to someone outside it, not to oneself.
        Assert.Null(registry.GroupSeatOf(call.Id, Ann, "tab-ann-2", Ben));
        Assert.Null(registry.GroupSeatOf(call.Id, Cy, "tab-cy", Ann));
        Assert.Null(registry.GroupSeatOf(call.Id, Ann, "tab-ann", Cy));
        Assert.Null(registry.GroupSeatOf(call.Id, Ann, "tab-ann", Ann));
        Assert.Null(registry.GroupSeatOf(Guid.NewGuid(), Ann, "tab-ann", Ben));

        registry.LeaveGroup(call.Id, Ben, Noon);
        Assert.Null(registry.GroupSeatOf(call.Id, Ann, "tab-ann", Ben));
    }

    // ── The line in the chat ──────────────────────────────────────────────────

    [Fact]
    public void A_group_call_is_recorded_as_a_message_from_whoever_started_it()
    {
        var done = ChatMessage.ForGroupCall(Trip, Ann, video: true, seconds: 135, tookPart: 3);
        var missed = ChatMessage.ForGroupCall(Trip, Ann, video: false, seconds: 40, tookPart: 1);

        Assert.True(done.IsCall);
        Assert.Equal(Ann, done.SenderId);
        Assert.Equal(CallOutcome.Completed, done.CallOutcome);
        Assert.Equal(135, done.CallSeconds);
        Assert.Equal(3, done.CallParticipants);
        Assert.True(done.CallVideo);
        Assert.Equal(string.Empty, done.Body);

        // Nobody but the starter ever joined: missed, with no length.
        Assert.Equal(CallOutcome.Missed, missed.CallOutcome);
        Assert.Equal(0, missed.CallSeconds);
        Assert.Equal(1, missed.CallParticipants);

        Assert.Throws<InvalidOperationException>(() => done.Edit("we never spoke"));
        // A call between two has no head count.
        Assert.Null(ChatMessage.ForCall(Trip, Ann, false, CallOutcome.Completed, 10).CallParticipants);
    }

    // ── The service: who may, who is rung, who is told ────────────────────────

    private readonly ChatWorld _world = new();

    private async Task<(User Sara, User Omar, User Lina, Guid GroupId)> GroupOfThreeAsync()
    {
        var sara = _world.Person("Sara Ali");
        var omar = _world.FriendOf("Omar Zaki", sara);
        var lina = _world.FriendOf("Lina Haddad", sara);
        var group = await _world.GroupsAs(sara).CreateAsync(new CreateGroupDto { Name = "Trip", MemberIds = { omar.Id, lina.Id } });

        _world.Realtime.Sent.Clear();
        return (sara, omar, lina, group.Id);
    }

    private static async Task<ApiException> RefusedAsync(Func<Task> action) =>
        await Assert.ThrowsAsync<ApiException>(action);

    [Fact]
    public async Task Starting_tells_everyone_in_the_chat_and_rings_the_members_who_are_online()
    {
        var (sara, omar, lina, groupId) = await GroupOfThreeAsync();
        _world.Presence.Online(sara, omar);

        var call = await _world.GroupCalls().StartAsync(sara.Id, "tab-sara", groupId, video: true);

        Assert.Equal(groupId, call.ConversationId);
        Assert.True(call.Video);
        Assert.Equal(sara.Id, call.StartedByUserId);
        var seat = Assert.Single(call.Participants);
        Assert.Equal("Sara Ali", seat.FullName);

        // "Call in progress" for all three; ringing only where someone can hear it.
        Assert.Equal(new HashSet<Guid> { sara.Id, omar.Id, lina.Id }, _world.Realtime.Heard(GroupCallService.Events.Started));
        Assert.Equal(new HashSet<Guid> { omar.Id }, _world.Realtime.Heard(GroupCallService.Events.Incoming));

        var invite = Assert.Single(_world.Realtime.Payloads<GroupCallInviteDto>(GroupCallService.Events.Incoming));
        Assert.Equal(call.CallId, invite.CallId);
        Assert.Equal("Trip", invite.GroupName);
        Assert.Equal(sara.Id, invite.From.Id);
        Assert.Equal("Sara Ali", invite.From.FullName);
    }

    [Fact]
    public async Task Nobody_on_another_call_or_across_a_block_is_rung()
    {
        var (sara, omar, lina, groupId) = await GroupOfThreeAsync();
        var nour = _world.FriendOf("Nour Saleh", omar);
        await _world.GroupsAs(omar).AddMembersAsync(groupId, new AddMembersDto { UserIds = { nour.Id } });
        _world.Realtime.Sent.Clear();
        _world.Presence.Online(sara, omar, lina, nour);

        // Omar is on a call between two; Lina has blocked Sara.
        _world.Registry.TryStart(Between(omar.Id, Guid.NewGuid(), DateTime.UtcNow), out _);
        _world.Block(lina, sara);

        await _world.GroupCalls().StartAsync(sara.Id, "tab-sara", groupId, video: false);

        Assert.Equal(new HashSet<Guid> { nour.Id }, _world.Realtime.Heard(GroupCallService.Events.Incoming));
        // They still see the call in the chat and may join later.
        Assert.Contains(lina.Id, _world.Realtime.Heard(GroupCallService.Events.Started));
        Assert.Contains(omar.Id, _world.Realtime.Heard(GroupCallService.Events.Started));
    }

    [Fact]
    public async Task Only_people_in_the_group_start_or_join_its_call()
    {
        var (sara, omar, _, groupId) = await GroupOfThreeAsync();
        var outsider = _world.FriendOf("Nour Saleh", sara);
        var calls = _world.GroupCalls();

        Assert.Equal(403, (await RefusedAsync(() => calls.StartAsync(outsider.Id, "tab-nour", groupId, false))).StatusCode);
        Assert.Equal(404, (await RefusedAsync(() => calls.StartAsync(sara.Id, "tab-sara", Guid.NewGuid(), false))).StatusCode);

        var call = await calls.StartAsync(sara.Id, "tab-sara", groupId, false);
        Assert.Equal(403, (await RefusedAsync(() => calls.JoinAsync(outsider.Id, "tab-nour", call.CallId))).StatusCode);
        Assert.Single(_world.Registry.GetGroup(call.CallId)!.Seats);

        // A chat between two has no group call.
        var direct = _world.DirectChat(sara, omar);
        Assert.Equal(400, (await RefusedAsync(() => calls.StartAsync(omar.Id, "tab-omar", direct.Id, false))).StatusCode);
    }

    [Fact]
    public async Task Joining_tells_the_chat_who_is_in_the_call_now()
    {
        var (sara, omar, lina, groupId) = await GroupOfThreeAsync();
        var calls = _world.GroupCalls();
        var call = await calls.StartAsync(sara.Id, "tab-sara", groupId, false);

        var joined = await calls.JoinAsync(omar.Id, "tab-omar", call.CallId);

        Assert.Equal(new[] { sara.Id, omar.Id }, joined.Participants.Select(p => p.Id));
        var change = Assert.Single(_world.Realtime.Payloads<GroupCallChangeDto>(GroupCallService.Events.Joined));
        Assert.Equal(omar.Id, change.UserId);
        Assert.Equal(new[] { "Sara Ali", "Omar Zaki" }, change.Participants.Select(p => p.FullName));
        Assert.Equal(new HashSet<Guid> { sara.Id, omar.Id, lina.Id }, _world.Realtime.Heard(GroupCallService.Events.Joined));

        // Pressing "start" while the group is on a call joins that call and rings nobody again.
        var third = await calls.StartAsync(lina.Id, "tab-lina", groupId, true);
        Assert.Equal(call.CallId, third.CallId);
        Assert.Equal(3, third.Participants.Count);
        Assert.Single(_world.Realtime.Sent, s => s.Event == GroupCallService.Events.Started);
    }

    [Fact]
    public async Task Busy_and_over_answer_409()
    {
        var (sara, omar, lina, groupId) = await GroupOfThreeAsync();
        var calls = _world.GroupCalls();
        var call = await calls.StartAsync(sara.Id, "tab-sara", groupId, false);

        _world.Registry.TryStart(Between(omar.Id, Guid.NewGuid(), DateTime.UtcNow), out _);
        var busy = await RefusedAsync(() => calls.JoinAsync(omar.Id, "tab-omar", call.CallId));
        Assert.Equal(409, busy.StatusCode);
        Assert.Equal("You are already on a call.", busy.Message);

        await calls.LeaveAsync(sara.Id, call.CallId);
        var over = await RefusedAsync(() => calls.JoinAsync(lina.Id, "tab-lina", call.CallId));
        Assert.Equal(409, over.StatusCode);
        Assert.Equal("That call is over.", over.Message);
    }

    [Fact]
    public async Task The_last_one_out_ends_the_call_and_it_is_written_into_the_chat()
    {
        var (sara, omar, lina, groupId) = await GroupOfThreeAsync();
        var calls = _world.GroupCalls();
        var call = await calls.StartAsync(sara.Id, "tab-sara", groupId, video: true);
        await calls.JoinAsync(omar.Id, "tab-omar", call.CallId);

        await calls.LeaveAsync(sara.Id, call.CallId);

        // Omar is still there: the call goes on and nothing is written yet.
        Assert.Empty(_world.Chat.GroupCallsLogged);
        Assert.Empty(_world.Realtime.Heard(GroupCallService.Events.Ended));
        var left = Assert.Single(_world.Realtime.Payloads<GroupCallChangeDto>(GroupCallService.Events.Left));
        Assert.Equal(sara.Id, left.UserId);
        Assert.Equal(new[] { omar.Id }, left.Participants.Select(p => p.Id));

        // His tab closes: he was the last.
        await calls.LeaveForConnectionAsync("tab-omar");

        Assert.Equal(new HashSet<Guid> { sara.Id, omar.Id, lina.Id }, _world.Realtime.Heard(GroupCallService.Events.Ended));
        var logged = Assert.Single(_world.Chat.GroupCallsLogged);
        Assert.Equal(groupId, logged.ConversationId);
        Assert.Equal(sara.Id, logged.StarterId);
        Assert.True(logged.Video);
        Assert.Equal(new HashSet<Guid> { sara.Id, omar.Id }, logged.TookPart.ToHashSet());
        Assert.Empty(_world.Registry.GetGroupsIn(new[] { groupId }));
    }

    [Fact]
    public async Task Someone_who_is_no_longer_in_the_group_still_hears_that_they_left_its_call()
    {
        var (sara, omar, _, groupId) = await GroupOfThreeAsync();
        var calls = _world.GroupCalls();
        var call = await calls.StartAsync(sara.Id, "tab-sara", groupId, false);
        await calls.JoinAsync(omar.Id, "tab-omar", call.CallId);

        // Removed from the group first, as the group chat service does it.
        _world.Participants.Remove(_world.Seat(groupId, omar));
        await calls.LeaveForMemberAsync(groupId, omar.Id);

        Assert.Contains(omar.Id, _world.Realtime.Heard(GroupCallService.Events.Left));
        Assert.Equal(new[] { sara.Id }, _world.Registry.GetGroup(call.CallId)!.Seats.Select(s => s.UserId));
    }

    [Fact]
    public async Task Dismissing_stops_the_ringing_for_that_person_only()
    {
        var (sara, omar, _, groupId) = await GroupOfThreeAsync();
        var outsider = _world.Person("Nour Saleh");
        var calls = _world.GroupCalls();
        var call = await calls.StartAsync(sara.Id, "tab-sara", groupId, false);

        await calls.DismissAsync(omar.Id, call.CallId);
        await calls.DismissAsync(outsider.Id, call.CallId);
        await calls.DismissAsync(omar.Id, Guid.NewGuid());

        var dismissed = Assert.Single(_world.Realtime.Sent, s => s.Event == GroupCallService.Events.Dismissed);
        Assert.Equal(new HashSet<Guid> { omar.Id }, dismissed.To);
        // The call goes on.
        Assert.NotNull(_world.Registry.GetGroup(call.CallId));
    }

    [Fact]
    public async Task Starting_calls_over_and_over_is_stopped()
    {
        var (sara, _, _, groupId) = await GroupOfThreeAsync();
        var calls = _world.GroupCalls();

        for (var i = 0; i < CallRegistry.MaxAttemptsPerMinute; i++)
        {
            var call = await calls.StartAsync(sara.Id, "tab-sara", groupId, false);
            await calls.LeaveAsync(sara.Id, call.CallId);
        }

        Assert.Equal(429, (await RefusedAsync(() => calls.StartAsync(sara.Id, "tab-sara", groupId, false))).StatusCode);
    }

    private static CallSession Between(Guid caller, Guid callee, DateTime at) => new()
    {
        ConversationId = Guid.NewGuid(), CallerId = caller, CalleeId = callee, CallerConnectionId = "tab-caller", StartedAt = at
    };
}
