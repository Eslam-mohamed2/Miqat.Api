using Miqat.Application.Common;
using Miqat.Application.Modules;
using Miqat.Application.Services;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Tests;

/// <summary>Who may be in a group chat, who runs it, and the lines its changes leave in the chat.</summary>
public class GroupChatTests
{
    private readonly ChatWorld _world = new();
    private readonly User _sara;
    private readonly User _omar;
    private readonly User _lina;

    public GroupChatTests()
    {
        _sara = _world.Person("Sara Ali");
        _omar = _world.FriendOf("Omar Zaki", _sara);
        _lina = _world.FriendOf("Lina Haddad", _sara);
    }

    private async Task<Guid> NewGroupAsync(string name = "Trip") =>
        (await _world.GroupsAs(_sara).CreateAsync(new CreateGroupDto { Name = name, MemberIds = { _omar.Id, _lina.Id } })).Id;

    private static async Task<int> StatusOfAsync(Func<Task> action) =>
        (await Assert.ThrowsAsync<ApiException>(action)).StatusCode;

    // ── Creating ──────────────────────────────────────────────────────────────

    [Fact]
    public void A_group_name_is_trimmed_and_has_1_to_60_characters()
    {
        Assert.Equal("Trip", Conversation.Group("  Trip ", _sara.Id).Name);
        Assert.Equal(60, Conversation.Group(new string('x', 60), _sara.Id).Name!.Length);

        Assert.Throws<ArgumentException>(() => Conversation.Group("   ", _sara.Id));
        Assert.Throws<ArgumentException>(() => Conversation.Group(new string('x', 61), _sara.Id));

        // The API answers 400 with this text, so it must not carry " (Parameter 'name')".
        Assert.Equal("A group needs a name.", Assert.Throws<ArgumentException>(() => Conversation.Group("", _sara.Id)).Message);
    }

    [Fact]
    public async Task The_creator_is_the_only_admin_and_the_chat_starts_with_a_created_line()
    {
        var id = await NewGroupAsync("  Trip ");

        var group = _world.Group(id);
        Assert.True(group.IsGroup);
        Assert.Equal("Trip", group.Name);
        Assert.Equal(_sara.Id, group.CreatedByUserId);
        Assert.Null(group.DirectKey);

        Assert.Equal(new[] { _sara.Id, _omar.Id, _lina.Id }, _world.PeopleIn(id).Select(p => p.UserId));
        Assert.Equal(new[] { _sara.Id }, _world.PeopleIn(id).Where(p => p.IsAdmin).Select(p => p.UserId));

        var line = Assert.Single(_world.Lines(id));
        Assert.Equal(ChatGroupEvent.Created, line.GroupEvent);
        Assert.Equal(_sara.Id, line.SenderId);
        Assert.Equal("Trip", line.GroupName);
        Assert.Equal("Sara Ali created the group \"Trip\"", line.Body);

        // The line is shown live, and it is unread for everyone but the creator.
        Assert.Equal(new[] { line.Id }, _world.Chat.Announced);
        Assert.Contains(id, _world.Chat.GroupsAnnounced);
        Assert.Equal(line.CreatedAt, group.LastMessageAt);
        Assert.Equal(line.CreatedAt, _world.Seat(id, _sara).LastReadAt);
        Assert.Null(_world.Seat(id, _omar).LastReadAt);
    }

    [Fact]
    public async Task A_group_starts_with_at_least_two_others_and_the_creator_is_not_listed()
    {
        var groups = _world.GroupsAs(_sara);

        Assert.Equal(400, await StatusOfAsync(() => groups.CreateAsync(new CreateGroupDto { Name = "Trip", MemberIds = { _omar.Id } })));
        // The same friend twice is still one person.
        Assert.Equal(400, await StatusOfAsync(() => groups.CreateAsync(new CreateGroupDto { Name = "Trip", MemberIds = { _omar.Id, _omar.Id } })));
        Assert.Equal(400, await StatusOfAsync(() => groups.CreateAsync(new CreateGroupDto { Name = "Trip", MemberIds = { _sara.Id, _omar.Id, _lina.Id } })));
        await Assert.ThrowsAsync<ArgumentException>(() => groups.CreateAsync(new CreateGroupDto { Name = " ", MemberIds = { _omar.Id, _lina.Id } }));

        Assert.Empty(_world.Conversations);
    }

    [Fact]
    public async Task Everyone_in_a_new_group_must_be_a_friend_of_the_creator()
    {
        var stranger = _world.Person("Nour Saleh");

        var status = await StatusOfAsync(() => _world.GroupsAs(_sara).CreateAsync(
            new CreateGroupDto { Name = "Trip", MemberIds = { _omar.Id, stranger.Id } }));

        Assert.Equal(403, status);
        Assert.Empty(_world.Conversations);
        Assert.Empty(_world.Participants);
    }

    [Fact]
    public async Task A_group_holds_at_most_50_people()
    {
        var friends = Enumerable.Range(1, Conversation.MaxGroupSize).Select(i => _world.FriendOf($"Friend {i}", _sara)).ToList();
        var groups = _world.GroupsAs(_sara);

        // 50 others and the creator would be 51.
        Assert.Equal(400, await StatusOfAsync(() => groups.CreateAsync(
            new CreateGroupDto { Name = "Everyone", MemberIds = friends.Select(f => f.Id).ToList() })));

        var id = (await groups.CreateAsync(
            new CreateGroupDto { Name = "Everyone", MemberIds = friends.Take(Conversation.MaxGroupSize - 1).Select(f => f.Id).ToList() })).Id;
        Assert.Equal(Conversation.MaxGroupSize, _world.PeopleIn(id).Count);

        Assert.Equal(400, await StatusOfAsync(() => groups.AddMembersAsync(id, new AddMembersDto { UserIds = { friends[^1].Id } })));
        Assert.Equal(Conversation.MaxGroupSize, _world.PeopleIn(id).Count);
    }

    // ── Adding ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Any_member_adds_friends_of_their_own()
    {
        var id = await NewGroupAsync();
        var nour = _world.FriendOf("Nour Saleh", _omar);

        // Omar is no admin, and Nour is no friend of the creator: neither matters.
        await _world.GroupsAs(_omar).AddMembersAsync(id, new AddMembersDto { UserIds = { nour.Id, _lina.Id } });

        Assert.Contains(nour.Id, _world.PeopleIn(id).Select(p => p.UserId));
        Assert.False(_world.Seat(id, nour).IsAdmin);
        // Lina was in the group already: skipped, not added twice.
        Assert.Equal(4, _world.PeopleIn(id).Count);

        var line = _world.Lines(id).Last();
        Assert.Equal(ChatGroupEvent.MemberAdded, line.GroupEvent);
        Assert.Equal(_omar.Id, line.SenderId);
        Assert.Equal(nour.Id, line.TargetUserId);
        Assert.Equal("Omar Zaki added Nour Saleh", line.Body);
        Assert.Contains(line.Id, _world.Chat.Announced);
    }

    [Fact]
    public async Task A_new_member_has_read_what_came_before_and_not_their_own_added_line()
    {
        var id = await NewGroupAsync();
        var nour = _world.FriendOf("Nour Saleh", _sara);

        await _world.GroupsAs(_sara).AddMembersAsync(id, new AddMembersDto { UserIds = { nour.Id } });

        var added = _world.Lines(id).Last();
        var readUpTo = _world.Seat(id, nour).LastReadAt;

        Assert.NotNull(readUpTo);
        Assert.True(readUpTo < added.CreatedAt);
        Assert.True(added.CreatedAt - readUpTo <= TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task Nobody_is_added_by_someone_who_is_not_their_friend()
    {
        var id = await NewGroupAsync();
        var stranger = _world.Person("Nour Saleh");

        Assert.Equal(403, await StatusOfAsync(() => _world.GroupsAs(_sara).AddMembersAsync(id, new AddMembersDto { UserIds = { stranger.Id } })));
        Assert.Equal(400, await StatusOfAsync(() => _world.GroupsAs(_sara).AddMembersAsync(id, new AddMembersDto())));
        Assert.Equal(3, _world.PeopleIn(id).Count);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Nobody_is_added_across_a_block_whoever_blocked_whom(bool theMemberBlocked)
    {
        var id = await NewGroupAsync();
        var nour = _world.FriendOf("Nour Saleh", _sara);

        if (theMemberBlocked) _world.Block(_sara, nour);
        else _world.Block(nour, _sara);

        Assert.Equal(403, await StatusOfAsync(() => _world.GroupsAs(_sara).AddMembersAsync(id, new AddMembersDto { UserIds = { nour.Id } })));
        Assert.DoesNotContain(nour.Id, _world.PeopleIn(id).Select(p => p.UserId));
    }

    [Fact]
    public async Task A_block_between_two_members_changes_nothing_inside_the_group()
    {
        var id = await NewGroupAsync();
        var nour = _world.FriendOf("Nour Saleh", _omar);
        _world.Block(nour, _sara);

        // Omar, a friend of both, may still bring Nour in.
        await _world.GroupsAs(_omar).AddMembersAsync(id, new AddMembersDto { UserIds = { nour.Id } });

        Assert.Equal(4, _world.PeopleIn(id).Count);
    }

    // ── Admins ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Only_an_admin_renames_removes_and_makes_admins()
    {
        var id = await NewGroupAsync();
        var asOmar = _world.GroupsAs(_omar);

        Assert.Equal(403, await StatusOfAsync(() => asOmar.RenameAsync(id, new RenameGroupDto { Name = "Mine now" })));
        Assert.Equal(403, await StatusOfAsync(() => asOmar.RemoveMemberAsync(id, _lina.Id)));
        Assert.Equal(403, await StatusOfAsync(() => asOmar.MakeAdminAsync(id, _omar.Id)));

        Assert.Equal("Trip", _world.Group(id).Name);
        Assert.Equal(3, _world.PeopleIn(id).Count);
        Assert.False(_world.Seat(id, _omar).IsAdmin);
    }

    [Fact]
    public async Task Renaming_leaves_a_line_and_the_same_name_again_does_not()
    {
        var id = await NewGroupAsync();
        var groups = _world.GroupsAs(_sara);

        await groups.RenameAsync(id, new RenameGroupDto { Name = " Trip 2027 " });

        var line = _world.Lines(id).Last();
        Assert.Equal("Trip 2027", _world.Group(id).Name);
        Assert.Equal(ChatGroupEvent.Renamed, line.GroupEvent);
        Assert.Equal("Trip 2027", line.GroupName);
        Assert.Equal("Sara Ali renamed the group to \"Trip 2027\"", line.Body);

        await groups.RenameAsync(id, new RenameGroupDto { Name = "Trip 2027" });
        Assert.Equal(2, _world.Lines(id).Count);

        await Assert.ThrowsAsync<ArgumentException>(() => groups.RenameAsync(id, new RenameGroupDto { Name = new string('x', 61) }));
    }

    [Fact]
    public async Task Removing_takes_the_person_out_of_the_group_and_out_of_its_call()
    {
        var id = await NewGroupAsync();

        await _world.GroupsAs(_sara).RemoveMemberAsync(id, _omar.Id);

        Assert.Equal(new[] { _sara.Id, _lina.Id }, _world.PeopleIn(id).Select(p => p.UserId));

        var line = _world.Lines(id).Last();
        Assert.Equal(ChatGroupEvent.MemberRemoved, line.GroupEvent);
        Assert.Equal(_omar.Id, line.TargetUserId);
        Assert.Equal("Sara Ali removed Omar Zaki", line.Body);

        // Omar's apps are told to drop the chat, and he leaves its call.
        Assert.Equal(new HashSet<Guid> { _omar.Id }, _world.Realtime.Heard(ChatService.Events.GroupRemoved));
        Assert.Equal(new[] { (id, _omar.Id) }, _world.CallsOfGroupChat.Left);
    }

    [Fact]
    public async Task An_admin_cannot_remove_themselves_or_someone_who_is_not_in_the_group()
    {
        var id = await NewGroupAsync();
        var stranger = _world.Person("Nour Saleh");
        var groups = _world.GroupsAs(_sara);

        Assert.Equal(400, await StatusOfAsync(() => groups.RemoveMemberAsync(id, _sara.Id)));
        Assert.Equal(404, await StatusOfAsync(() => groups.RemoveMemberAsync(id, stranger.Id)));
        Assert.Equal(404, await StatusOfAsync(() => groups.MakeAdminAsync(id, stranger.Id)));
    }

    [Fact]
    public async Task A_member_made_admin_can_do_what_admins_do()
    {
        var id = await NewGroupAsync();

        await _world.GroupsAs(_sara).MakeAdminAsync(id, _omar.Id);
        Assert.True(_world.Seat(id, _omar).IsAdmin);
        Assert.True(_world.Seat(id, _sara).IsAdmin);

        await _world.GroupsAs(_omar).RemoveMemberAsync(id, _lina.Id);
        Assert.DoesNotContain(_lina.Id, _world.PeopleIn(id).Select(p => p.UserId));
    }

    // ── Leaving ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Leaving_leaves_a_line_and_the_call()
    {
        var id = await NewGroupAsync();

        await _world.GroupsAs(_lina).LeaveAsync(id);

        Assert.Equal(new[] { _sara.Id, _omar.Id }, _world.PeopleIn(id).Select(p => p.UserId));

        var line = _world.Lines(id).Last();
        Assert.Equal(ChatGroupEvent.MemberLeft, line.GroupEvent);
        Assert.Equal(_lina.Id, line.SenderId);
        Assert.Null(line.TargetUserId);
        Assert.Equal("Lina Haddad left the group", line.Body);

        Assert.Equal(new HashSet<Guid> { _lina.Id }, _world.Realtime.Heard(ChatService.Events.GroupRemoved));
        Assert.Equal(new[] { (id, _lina.Id) }, _world.CallsOfGroupChat.Left);
        // An admin is still there: nobody is promoted.
        Assert.Equal(new[] { _sara.Id }, _world.PeopleIn(id).Where(p => p.IsAdmin).Select(p => p.UserId));
    }

    [Fact]
    public async Task When_the_last_admin_leaves_the_member_who_has_been_in_longest_becomes_admin()
    {
        var id = await NewGroupAsync();
        await _world.GroupsAs(_lina).LeaveAsync(id);

        // Nour joins later than Omar, who was there from the start.
        Thread.Sleep(5);
        var nour = _world.FriendOf("Nour Saleh", _sara);
        await _world.GroupsAs(_sara).AddMembersAsync(id, new AddMembersDto { UserIds = { nour.Id } });

        await _world.GroupsAs(_sara).LeaveAsync(id);

        Assert.Equal(new[] { _omar.Id }, _world.PeopleIn(id).Where(p => p.IsAdmin).Select(p => p.UserId));
    }

    [Fact]
    public async Task While_another_admin_stays_nobody_is_promoted()
    {
        var id = await NewGroupAsync();
        await _world.GroupsAs(_sara).MakeAdminAsync(id, _lina.Id);

        await _world.GroupsAs(_sara).LeaveAsync(id);

        Assert.Equal(new[] { _lina.Id }, _world.PeopleIn(id).Where(p => p.IsAdmin).Select(p => p.UserId));
    }

    [Fact]
    public void The_next_admin_is_the_longest_standing_member_and_only_when_there_is_no_admin()
    {
        var chat = Guid.NewGuid();
        var first = new ConversationParticipant(chat, Guid.NewGuid());
        Thread.Sleep(5);
        var second = new ConversationParticipant(chat, Guid.NewGuid());

        Assert.Same(first, Conversation.NextAdmin(new[] { second, first }));
        Assert.Null(Conversation.NextAdmin(Array.Empty<ConversationParticipant>()));

        second.MakeAdmin();
        Assert.Null(Conversation.NextAdmin(new[] { second, first }));
        Assert.False(second.MakeAdmin());
    }

    [Fact]
    public async Task A_group_with_nobody_left_is_deleted()
    {
        var id = await NewGroupAsync();

        await _world.GroupsAs(_sara).LeaveAsync(id);
        await _world.GroupsAs(_omar).LeaveAsync(id);
        Assert.Single(_world.PeopleIn(id));

        await _world.GroupsAs(_lina).LeaveAsync(id);

        Assert.Empty(_world.Conversations);
        Assert.Empty(_world.PeopleIn(id));
    }

    [Fact]
    public async Task Someone_who_left_can_be_added_again()
    {
        var id = await NewGroupAsync();
        await _world.GroupsAs(_omar).LeaveAsync(id);

        await _world.GroupsAs(_sara).AddMembersAsync(id, new AddMembersDto { UserIds = { _omar.Id } });

        Assert.Contains(_omar.Id, _world.PeopleIn(id).Select(p => p.UserId));
    }

    // ── Who may ask at all ────────────────────────────────────────────────────

    [Fact]
    public async Task An_outsider_gets_403_a_missing_group_404_and_a_chat_between_two_400()
    {
        var id = await NewGroupAsync();
        var outsider = _world.FriendOf("Nour Saleh", _sara);
        var asOutsider = _world.GroupsAs(outsider);

        Assert.Equal(403, await StatusOfAsync(() => asOutsider.AddMembersAsync(id, new AddMembersDto { UserIds = { _sara.Id } })));
        Assert.Equal(403, await StatusOfAsync(() => asOutsider.LeaveAsync(id)));
        Assert.Equal(403, await StatusOfAsync(() => asOutsider.RenameAsync(id, new RenameGroupDto { Name = "x" })));

        Assert.Equal(404, await StatusOfAsync(() => _world.GroupsAs(_sara).LeaveAsync(Guid.NewGuid())));

        var direct = _world.DirectChat(_sara, _omar);
        Assert.Equal(400, await StatusOfAsync(() => _world.GroupsAs(_sara).LeaveAsync(direct.Id)));
        Assert.Equal(400, await StatusOfAsync(() => _world.GroupsAs(_sara).AddMembersAsync(direct.Id, new AddMembersDto { UserIds = { _lina.Id } })));
        Assert.Throws<InvalidOperationException>(() => direct.Rename("Us two"));
    }

    [Fact]
    public void A_line_about_the_group_cannot_be_edited()
    {
        var line = ChatMessage.ForGroupEvent(Guid.NewGuid(), _sara.Id, ChatGroupEvent.MemberAdded, "Sara Ali added Omar Zaki", _omar.Id);

        Assert.True(line.IsGroupEvent);
        Assert.False(line.IsCall);
        Assert.Throws<InvalidOperationException>(() => line.Edit("Sara Ali added nobody"));
        Assert.False(new ChatMessage(Guid.NewGuid(), _sara.Id, "hi").IsGroupEvent);
    }
}
