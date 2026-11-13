using Miqat.Application.Common;
using Miqat.Application.Modules;
using Miqat.Application.Services;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Tests;

/// <summary>
/// A project's discussion: who may read and write it, what a comment may be,
/// replies, who edits and deletes, pages, and who is told about what.
/// </summary>
public class GroupCommentTests
{
    private readonly ProjectWorld _world = new();
    private readonly User _sara;   // owns the project
    private readonly User _omar;   // member
    private readonly User _lina;   // member
    private readonly User _nour;   // not on the project
    private readonly Group _project;

    public GroupCommentTests()
    {
        _sara = _world.Person("Sara Ali");
        _omar = _world.Person("Omar Zaki");
        _lina = _world.Person("Lina Haddad");
        _nour = _world.Person("Nour Saleh");
        _project = _world.Project("Website", _sara, _omar, _lina);
    }

    private static async Task<ApiException> RefusedAsync(Func<Task> action) =>
        await Assert.ThrowsAsync<ApiException>(action);

    private static async Task<int> StatusOfAsync(Func<Task> action) => (await RefusedAsync(action)).StatusCode;

    private Task<GroupCommentPageDto> PageAsync(User reader, Guid? before = null, int? limit = null) =>
        _world.As(reader).GetPageAsync(_project.Id, before, limit);

    // ── Who may read and write ───────────────────────────────────────────────

    [Fact]
    public async Task The_owner_and_the_members_read_and_write_the_discussion()
    {
        var first = await _world.SayAsync(_sara, _project, "Kick-off is on Monday.", minutesAgo: 2);
        var second = await _world.SayAsync(_omar, _project, "I will bring the wireframes.", minutesAgo: 1);

        var page = await PageAsync(_lina);

        Assert.Equal(new[] { first.Id, second.Id }, page.Comments.Select(c => c.Id));
        Assert.Equal(2, page.Total);
        Assert.False(page.HasMore);

        var comment = page.Comments[1];
        Assert.Equal("I will bring the wireframes.", comment.Content);
        Assert.Equal(_project.Id, comment.GroupId);
        Assert.Equal(_omar.Id, comment.AuthorId);
        Assert.Equal("Omar Zaki", comment.AuthorName);
        Assert.Null(comment.ParentId);
        Assert.Null(comment.UpdatedAt);
        Assert.False(comment.IsEdited);
    }

    [Fact]
    public async Task Someone_who_is_not_on_the_project_gets_403_on_every_route()
    {
        var comment = await _world.SayAsync(_omar, _project, "Members only.");
        var outsider = _world.As(_nour);

        var refusals = new[]
        {
            await RefusedAsync(() => outsider.GetPageAsync(_project.Id, null, null)),
            await RefusedAsync(() => outsider.AddAsync(_project.Id, new CreateGroupCommentDto { Content = "Hello" })),
            await RefusedAsync(() => outsider.GetMentionableAsync(_project.Id)),
            await RefusedAsync(() => outsider.UpdateAsync(comment.Id, "Changed")),
            await RefusedAsync(() => outsider.DeleteAsync(comment.Id))
        };

        Assert.All(refusals, refusal =>
        {
            Assert.Equal(403, refusal.StatusCode);
            // The answer the project itself gives to someone who is not on it.
            Assert.Equal("You do not have access to this project.", refusal.Message);
        });

        Assert.Equal("Members only.", _world.Row(comment.Id).Content);
        Assert.Single(_world.Comments);
    }

    [Fact]
    public async Task A_project_that_does_not_exist_or_was_deleted_answers_404()
    {
        var comment = await _world.SayAsync(_sara, _project, "Before the project went away.");
        var owner = _world.As(_sara);

        Assert.Equal(404, await StatusOfAsync(() => owner.GetPageAsync(Guid.NewGuid(), null, null)));
        Assert.Equal(404, await StatusOfAsync(() => owner.AddAsync(Guid.NewGuid(), new CreateGroupCommentDto { Content = "Hello" })));

        _project.SoftDelete();

        Assert.Equal(404, await StatusOfAsync(() => owner.GetPageAsync(_project.Id, null, null)));
        Assert.Equal(404, await StatusOfAsync(() => owner.AddAsync(_project.Id, new CreateGroupCommentDto { Content = "Hello" })));
        Assert.Equal(404, await StatusOfAsync(() => owner.GetMentionableAsync(_project.Id)));

        // A comment of a deleted project is gone with it: the controller answers 404.
        Assert.Null(await owner.UpdateAsync(comment.Id, "Changed"));
        Assert.False(await owner.DeleteAsync(comment.Id));
        Assert.Null(await owner.UpdateAsync(Guid.NewGuid(), "Changed"));
        Assert.False(await owner.DeleteAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task A_member_who_was_removed_loses_the_discussion_and_their_own_comments_in_it()
    {
        var comment = await _world.SayAsync(_omar, _project, "Written while on the project.");

        _world.Remove(_project, _omar);
        var removed = _world.As(_omar);

        Assert.Equal(403, await StatusOfAsync(() => removed.GetPageAsync(_project.Id, null, null)));
        Assert.Equal(403, await StatusOfAsync(() => removed.UpdateAsync(comment.Id, "Changed")));
        Assert.Equal(403, await StatusOfAsync(() => removed.DeleteAsync(comment.Id)));

        // What they wrote stays for the team.
        Assert.Equal(new[] { comment.Id }, (await PageAsync(_sara)).Comments.Select(c => c.Id));
    }

    // ── What a comment may be ────────────────────────────────────────────────

    [Fact]
    public async Task A_comment_has_the_limits_of_a_task_comment()
    {
        var member = _world.As(_omar);

        foreach (var empty in new[] { "", "   ", "\n\t" })
        {
            var refusal = await RefusedAsync(() => member.AddAsync(_project.Id, new CreateGroupCommentDto { Content = empty }));
            Assert.Equal(400, refusal.StatusCode);
            Assert.Equal("A comment cannot be empty.", refusal.Message);
        }

        var tooLong = await RefusedAsync(() =>
            member.AddAsync(_project.Id, new CreateGroupCommentDto { Content = new string('x', 2001) }));
        Assert.Equal(400, tooLong.StatusCode);
        Assert.Equal("Comments are limited to 2000 characters.", tooLong.Message);
        Assert.Empty(_world.Comments);

        var longest = await member.AddAsync(_project.Id, new CreateGroupCommentDto { Content = new string('x', 2000) });
        Assert.Equal(2000, longest.Content.Length);

        var trimmed = await member.AddAsync(_project.Id, new CreateGroupCommentDto { Content = "  Hello team \n" });
        Assert.Equal("Hello team", trimmed.Content);

        // The same limits when a comment is rewritten.
        Assert.Equal(400, await StatusOfAsync(() => member.UpdateAsync(trimmed.Id, "  ")));
        Assert.Equal(400, await StatusOfAsync(() => member.UpdateAsync(trimmed.Id, new string('x', 2001))));
        Assert.Equal("Hello team", _world.Row(trimmed.Id).Content);
    }

    [Fact]
    public async Task The_id_the_app_gave_a_comment_comes_back_and_is_limited_to_64_characters()
    {
        var member = _world.As(_omar);

        var sent = await member.AddAsync(_project.Id, new CreateGroupCommentDto { Content = "Hello", ClientId = " local-1 " });
        Assert.Equal("local-1", sent.ClientId);

        Assert.Equal(400, await StatusOfAsync(() =>
            member.AddAsync(_project.Id, new CreateGroupCommentDto { Content = "Hello", ClientId = new string('c', 65) })));

        // It is not stored: a page read later does not carry it.
        Assert.Null((await PageAsync(_omar)).Comments.Single().ClientId);
    }

    // ── Replies ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Replies_follow_the_comment_they_answer_oldest_first()
    {
        var plan = await _world.SayAsync(_sara, _project, "Plan for the week?", minutesAgo: 50);
        var budget = await _world.SayAsync(_omar, _project, "Budget is approved.", minutesAgo: 40);
        // Written out of order on purpose: a reply belongs under its comment, whenever it was written.
        var budgetReply = await _world.SayAsync(_lina, _project, "Great news.", budget.Id, minutesAgo: 30);
        var planReply1 = await _world.SayAsync(_omar, _project, "Design first.", plan.Id, minutesAgo: 20);
        var planReply2 = await _world.SayAsync(_lina, _project, "Then the build.", plan.Id, minutesAgo: 10);

        var page = await PageAsync(_sara);

        Assert.Equal(
            new[] { plan.Id, planReply1.Id, planReply2.Id, budget.Id, budgetReply.Id },
            page.Comments.Select(c => c.Id));
        Assert.Equal(new Guid?[] { null, plan.Id, plan.Id, null, budget.Id }, page.Comments.Select(c => c.ParentId));
        Assert.Equal(5, page.Total);
    }

    [Fact]
    public async Task Answering_a_reply_continues_under_the_same_comment()
    {
        var comment = await _world.SayAsync(_sara, _project, "Who takes the landing page?", minutesAgo: 3);
        var reply = await _world.SayAsync(_omar, _project, "I can.", comment.Id, minutesAgo: 2);

        var answer = await _world.SayAsync(_lina, _project, "Thanks Omar!", reply.Id);

        // One level deep: no reply ever has replies of its own.
        Assert.Equal(comment.Id, answer.ParentId);
        Assert.All(_world.Comments.Where(c => c.ParentId != null), c => Assert.Null(_world.Row(c.ParentId!.Value).ParentId));
    }

    [Fact]
    public async Task A_reply_needs_a_comment_that_is_still_in_this_project()
    {
        var elsewhere = _world.Project("Mobile app", _sara, _omar);
        var foreign = await _world.SayAsync(_sara, elsewhere, "In the other project.");
        var gone = await _world.SayAsync(_sara, _project, "Deleted in a moment.");
        await _world.As(_sara).DeleteAsync(gone.Id);

        var member = _world.As(_omar);
        foreach (var parentId in new[] { foreign.Id, gone.Id, Guid.NewGuid() })
        {
            Assert.Equal(400, await StatusOfAsync(() =>
                member.AddAsync(_project.Id, new CreateGroupCommentDto { Content = "A reply", ParentId = parentId })));
        }

        Assert.DoesNotContain(_world.Comments, c => c.Content == "A reply");
    }

    // ── Editing and deleting ─────────────────────────────────────────────────

    [Fact]
    public async Task Only_the_author_rewrites_a_comment()
    {
        var comment = await _world.SayAsync(_omar, _project, "Frist draft.");

        // Not the owner, not a fellow member.
        foreach (var other in new[] { _sara, _lina })
        {
            var refusal = await RefusedAsync(() => _world.As(other).UpdateAsync(comment.Id, "Rewritten"));
            Assert.Equal(403, refusal.StatusCode);
            Assert.Equal("Only the comment's author can edit it.", refusal.Message);
        }
        Assert.Equal("Frist draft.", _world.Row(comment.Id).Content);

        var saved = await _world.As(_omar).UpdateAsync(comment.Id, "  First draft. ");

        Assert.NotNull(saved);
        Assert.Equal("First draft.", saved!.Content);
        Assert.True(saved.IsEdited);
        Assert.NotNull(saved.UpdatedAt);
        Assert.True(saved.CanEdit);
        Assert.True((await PageAsync(_lina)).Comments.Single().IsEdited);
    }

    [Fact]
    public async Task The_author_and_the_owner_delete_a_comment_and_a_fellow_member_does_not()
    {
        var byOmar = await _world.SayAsync(_omar, _project, "Mine to delete.", minutesAgo: 2);
        var alsoByOmar = await _world.SayAsync(_omar, _project, "The owner may delete this.", minutesAgo: 1);

        var refusal = await RefusedAsync(() => _world.As(_lina).DeleteAsync(byOmar.Id));
        Assert.Equal(403, refusal.StatusCode);
        Assert.Equal("Only the comment's author or the project owner can delete it.", refusal.Message);
        Assert.False(_world.Row(byOmar.Id).IsDeleted);

        Assert.True(await _world.As(_omar).DeleteAsync(byOmar.Id));
        Assert.True(await _world.As(_sara).DeleteAsync(alsoByOmar.Id));

        var page = await PageAsync(_lina);
        Assert.Empty(page.Comments);
        Assert.Equal(0, page.Total);

        // Deleting twice finds nothing: the controller answers 404.
        Assert.False(await _world.As(_omar).DeleteAsync(byOmar.Id));
    }

    [Fact]
    public async Task An_app_admin_may_delete_but_not_rewrite()
    {
        var admin = _world.Person("Adam Admin", UserRole.Admin);
        var comment = await _world.SayAsync(_omar, _project, "Reported comment.");

        Assert.Equal(403, await StatusOfAsync(() => _world.As(admin).UpdateAsync(comment.Id, "Rewritten")));
        Assert.True(await _world.As(admin).DeleteAsync(comment.Id));
    }

    [Fact]
    public async Task Deleting_a_comment_deletes_the_replies_to_it_and_nothing_else()
    {
        var kept = await _world.SayAsync(_sara, _project, "Stays.", minutesAgo: 9);
        var keptReply = await _world.SayAsync(_lina, _project, "Stays too.", kept.Id, minutesAgo: 8);
        var comment = await _world.SayAsync(_omar, _project, "Goes.", minutesAgo: 7);
        var reply1 = await _world.SayAsync(_lina, _project, "Goes with it.", comment.Id, minutesAgo: 6);
        var reply2 = await _world.SayAsync(_sara, _project, "And this.", comment.Id, minutesAgo: 5);

        Assert.True(await _world.As(_omar).DeleteAsync(comment.Id));

        Assert.All(new[] { comment.Id, reply1.Id, reply2.Id }, id => Assert.True(_world.Row(id).IsDeleted));

        var page = await PageAsync(_sara);
        Assert.Equal(new[] { kept.Id, keptReply.Id }, page.Comments.Select(c => c.Id));
        Assert.Equal(2, page.Total);

        // Deleting a reply leaves its comment and the other replies alone.
        var another = await _world.SayAsync(_omar, _project, "One more.", kept.Id);
        Assert.True(await _world.As(_lina).DeleteAsync(keptReply.Id));
        Assert.Equal(new[] { kept.Id, another.Id }, (await PageAsync(_sara)).Comments.Select(c => c.Id));
    }

    [Fact]
    public async Task Each_reader_is_told_what_they_may_do_with_each_comment()
    {
        await _world.SayAsync(_sara, _project, "By the owner.", minutesAgo: 2);
        await _world.SayAsync(_omar, _project, "By a member.", minutesAgo: 1);

        (bool, bool)[] Rights(GroupCommentPageDto page) => page.Comments.Select(c => (c.CanEdit, c.CanDelete)).ToArray();

        // The owner edits her own and deletes anything.
        Assert.Equal(new[] { (true, true), (false, true) }, Rights(await PageAsync(_sara)));
        // A member edits and deletes his own.
        Assert.Equal(new[] { (false, false), (true, true) }, Rights(await PageAsync(_omar)));
        Assert.Equal(new[] { (false, false), (false, false) }, Rights(await PageAsync(_lina)));
    }

    // ── Pages ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_long_discussion_is_read_a_page_at_a_time_from_the_newest_back()
    {
        // 65 comments, number 1 the oldest; a few of them have replies.
        var ids = new List<Guid>();
        for (var n = 1; n <= 65; n++)
            ids.Add((await _world.SayAsync(n % 2 == 0 ? _omar : _sara, _project, $"Comment {n}", minutesAgo: 1000 - n * 10)).Id);

        var replyToOldest = await _world.SayAsync(_lina, _project, "Reply to 1", ids[0], minutesAgo: 5);
        var replyToNewest = await _world.SayAsync(_lina, _project, "Reply to 65", ids[64], minutesAgo: 4);

        // The newest 30 (36..65), oldest first, with the reply to 65 under it.
        var newest = await PageAsync(_omar);
        Assert.True(newest.HasMore);
        Assert.Equal(67, newest.Total);
        Assert.Equal(ids.Skip(35).Append(replyToNewest.Id), newest.Comments.Select(c => c.Id));

        // Scrolling up: "before" is the first comment on screen.
        var middle = await PageAsync(_omar, before: newest.Comments[0].Id);
        Assert.True(middle.HasMore);
        Assert.Equal(ids.Skip(5).Take(30), middle.Comments.Select(c => c.Id));

        // The last page: the five oldest, the reply to number 1 right under it.
        var oldest = await PageAsync(_omar, before: middle.Comments[0].Id);
        Assert.False(oldest.HasMore);
        Assert.Equal(
            new[] { ids[0], replyToOldest.Id, ids[1], ids[2], ids[3], ids[4] },
            oldest.Comments.Select(c => c.Id));

        // Nothing was skipped and nothing came twice.
        var all = oldest.Comments.Concat(middle.Comments).Concat(newest.Comments).Select(c => c.Id).ToList();
        Assert.Equal(67, all.Distinct().Count());
        Assert.Equal(67, all.Count);
    }

    [Fact]
    public async Task The_page_size_is_the_callers_choice_between_1_and_100()
    {
        var ids = new List<Guid>();
        for (var n = 1; n <= 105; n++)
            ids.Add((await _world.SayAsync(_sara, _project, $"Comment {n}", minutesAgo: 2000 - n * 10)).Id);

        var ten = await PageAsync(_omar, limit: 10);
        Assert.Equal(ids.Skip(95), ten.Comments.Select(c => c.Id));
        Assert.True(ten.HasMore);

        Assert.Single((await PageAsync(_omar, limit: 0)).Comments);
        Assert.Single((await PageAsync(_omar, limit: -5)).Comments);
        Assert.Equal(100, (await PageAsync(_omar, limit: 500)).Comments.Count);
        Assert.Equal(GroupCommentService.DefaultPageSize, (await PageAsync(_omar)).Comments.Count);

        // An exact fit says there is nothing more.
        var exact = await PageAsync(_omar, before: ids[5], limit: 5);
        Assert.Equal(ids.Take(5), exact.Comments.Select(c => c.Id));
        Assert.False(exact.HasMore);
    }

    [Fact]
    public async Task The_place_to_continue_from_may_be_a_reply_or_a_comment_deleted_since()
    {
        var one = await _world.SayAsync(_sara, _project, "One", minutesAgo: 40);
        var two = await _world.SayAsync(_sara, _project, "Two", minutesAgo: 30);
        var three = await _world.SayAsync(_sara, _project, "Three", minutesAgo: 20);
        var replyToThree = await _world.SayAsync(_omar, _project, "Reply to three", three.Id, minutesAgo: 10);

        // A reply stands for the comment it answers.
        Assert.Equal(new[] { one.Id, two.Id }, (await PageAsync(_omar, before: replyToThree.Id)).Comments.Select(c => c.Id));

        // Someone deletes the comment at the top of the screen: scrolling up still works.
        await _world.As(_sara).DeleteAsync(two.Id);
        Assert.Equal(new[] { one.Id }, (await PageAsync(_omar, before: two.Id)).Comments.Select(c => c.Id));

        // A comment of another project is not a place in this one.
        var elsewhere = _world.Project("Mobile app", _sara, _omar);
        var foreign = await _world.SayAsync(_sara, elsewhere, "Elsewhere");
        Assert.Equal(400, await StatusOfAsync(() => PageAsync(_omar, before: foreign.Id)));
        Assert.Equal(400, await StatusOfAsync(() => PageAsync(_omar, before: Guid.NewGuid())));
    }

    [Fact]
    public async Task A_project_says_how_many_comments_its_discussion_has()
    {
        var comment = await _world.SayAsync(_sara, _project, "One");
        await _world.SayAsync(_omar, _project, "Two", comment.Id);
        var third = await _world.SayAsync(_lina, _project, "Three");
        await _world.As(_lina).DeleteAsync(third.Id);

        var quiet = _world.Project("Mobile app", _sara, _omar);

        // Deleted comments are not counted; replies are.
        var projects = (await _world.ProjectsAs(_sara).GetAllGroups(_sara.Id)).ToDictionary(p => p.Id);
        Assert.Equal(2, projects[_project.Id].CommentCount);
        Assert.Equal(0, projects[quiet.Id].CommentCount);
        Assert.Equal(2, (await _world.ProjectsAs(_omar).GetGroupById(_project.Id))!.CommentCount);
    }

    // ── Mentions and notifications ───────────────────────────────────────────

    [Fact]
    public async Task Everyone_on_the_project_but_the_caller_can_be_mentioned()
    {
        var people = (await _world.As(_omar).GetMentionableAsync(_project.Id)).ToList();

        Assert.Equal(new[] { "Lina Haddad", "Sara Ali" }, people.Select(p => p.FullName));
        Assert.Equal(new[] { "Project member", "Project owner" }, people.Select(p => p.Relationship));
        // Nobody is let in by a mention here: they are all on the project already.
        Assert.All(people, p => Assert.True(p.HasAccess));
        Assert.Equal(new[] { _lina.Id, _sara.Id }, people.Select(p => p.UserId));
    }

    [Fact]
    public async Task A_mention_notifies_the_member_and_points_at_the_comment_in_the_project()
    {
        var comment = await _world.As(_omar).AddAsync(_project.Id, new CreateGroupCommentDto
        {
            Content = "@Lina Haddad can you check the copy?",
            // Herself, a stranger and a made-up id are ignored; a member named twice is told once.
            MentionedUserIds = { _lina.Id, _lina.Id, _omar.Id, _nour.Id, Guid.NewGuid(), Guid.Empty }
        });

        var notification = Assert.Single(_world.Notifications.Sent);
        Assert.Equal(_lina.Id, notification.RecipientUserId);
        Assert.Equal(_omar.Id, notification.TriggeredByUserId);
        Assert.Equal(NotificationType.MentionedInTask, notification.Type);
        Assert.Equal("You were mentioned", notification.Title);
        Assert.Equal("Someone mentioned you in \"Website\".", notification.Message);
        // Opens the project; the comment is the place inside its discussion.
        Assert.Equal("Group", notification.LinkedEntityType);
        Assert.Equal(_project.Id, notification.LinkedEntityId);
        Assert.Equal(comment.Id, notification.LinkedCommentId);

        var mention = Assert.Single(_world.Mentions);
        Assert.Equal((_omar.Id, _lina.Id), (mention.MentionedByUserId, mention.MentionedUserId));
        Assert.Equal((EntityType.Project, _project.Id), (mention.EntityType, mention.EntityId));
        Assert.False(mention.IsRead);
    }

    [Fact]
    public async Task Mentioning_someone_again_tells_them_again_without_piling_up_unread_mentions()
    {
        Task MentionLinaAsync() => _world.As(_omar).AddAsync(
            _project.Id, new CreateGroupCommentDto { Content = "@Lina Haddad ping", MentionedUserIds = { _lina.Id } });

        await MentionLinaAsync();
        await MentionLinaAsync();

        Assert.Equal(2, _world.Notifications.For(_lina).Count);
        Assert.Single(_world.Mentions);

        // Once she has read it, the next mention is a new one.
        _world.Mentions.Single().MarkAsRead();
        await MentionLinaAsync();
        Assert.Equal(2, _world.Mentions.Count);
    }

    [Fact]
    public async Task A_reply_is_news_for_the_author_of_the_comment_it_answers()
    {
        var comment = await _world.SayAsync(_sara, _project, "Can we ship on Friday?");
        Assert.Empty(_world.Notifications.Sent);

        var reply = await _world.SayAsync(_omar, _project, "Yes, if the review is done by Thursday.", comment.Id);

        var notification = Assert.Single(_world.Notifications.Sent);
        Assert.Equal(_sara.Id, notification.RecipientUserId);
        Assert.Equal("New reply", notification.Title);
        Assert.Equal("Someone replied to your comment in \"Website\": Yes, if the review is done by Thursday.", notification.Message);
        Assert.Equal(("Group", _project.Id, reply.Id),
            (notification.LinkedEntityType, notification.LinkedEntityId!.Value, notification.LinkedCommentId!.Value));

        // Answering yourself is not news, and neither is a comment that answers nobody.
        await _world.SayAsync(_sara, _project, "Thursday it is.", comment.Id);
        await _world.SayAsync(_lina, _project, "Unrelated.");
        Assert.Single(_world.Notifications.For(_sara));
        Assert.Empty(_world.Notifications.For(_omar));
        Assert.Empty(_world.Mentions);
    }

    [Fact]
    public async Task A_reply_that_also_mentions_the_author_tells_them_once()
    {
        var comment = await _world.SayAsync(_sara, _project, "Thoughts?");

        await _world.As(_omar).AddAsync(_project.Id, new CreateGroupCommentDto
        {
            Content = "@Sara Ali looks good, @Lina Haddad agrees",
            ParentId = comment.Id,
            MentionedUserIds = { _sara.Id, _lina.Id }
        });

        Assert.Equal("You were mentioned", Assert.Single(_world.Notifications.For(_sara)).Title);
        Assert.Equal("You were mentioned", Assert.Single(_world.Notifications.For(_lina)).Title);
    }

    [Fact]
    public async Task A_reply_does_not_reach_an_author_who_left_the_project()
    {
        var comment = await _world.SayAsync(_omar, _project, "My last words here.");
        _world.Remove(_project, _omar);

        await _world.SayAsync(_lina, _project, "Goodbye!", comment.Id);

        Assert.Empty(_world.Notifications.Sent);
    }

    // ── Live ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_new_comment_is_shown_live_to_everyone_on_the_project_with_their_own_rights()
    {
        var sent = await _world.As(_omar).AddAsync(
            _project.Id, new CreateGroupCommentDto { Content = "Hello team", ClientId = "local-7" });

        var events = _world.Events();

        // Everyone on the project, the writer included (his other tabs) - and nobody else.
        Assert.Equal(
            new[] { _sara.Id, _omar.Id, _lina.Id }.OrderBy(id => id),
            events.SelectMany(e => e.To).OrderBy(id => id));

        Assert.All(events, e =>
        {
            Assert.Equal("added", e.Event.Action);
            Assert.Equal(_project.Id, e.Event.GroupId);
            Assert.Equal(1, e.Event.Total);
            Assert.Equal(sent.Id, e.Event.Comment!.Id);
            Assert.Equal("Hello team", e.Event.Comment.Content);
            Assert.Equal("Omar Zaki", e.Event.Comment.AuthorName);
            Assert.Equal("local-7", e.Event.Comment.ClientId);
            Assert.Empty(e.Event.DeletedIds);
        });

        (bool, bool) RightsOf(User user)
        {
            var comment = events.Single(e => e.To.Contains(user.Id)).Event.Comment!;
            return (comment.CanEdit, comment.CanDelete);
        }

        Assert.Equal((true, true), RightsOf(_omar));     // the author
        Assert.Equal((false, true), RightsOf(_sara));    // the owner
        Assert.Equal((false, false), RightsOf(_lina));   // a fellow member
    }

    [Fact]
    public async Task Edits_and_deletions_are_shown_live_too()
    {
        var comment = await _world.SayAsync(_omar, _project, "Frist", minutesAgo: 3);
        var reply = await _world.SayAsync(_lina, _project, "A reply", comment.Id, minutesAgo: 2);
        _world.Realtime.Sent.Clear();

        await _world.As(_omar).UpdateAsync(comment.Id, "First");

        var edits = _world.Events();
        Assert.All(edits, e => Assert.Equal(("edited", "First", true, 2), (e.Event.Action, e.Event.Comment!.Content, e.Event.Comment.IsEdited, e.Event.Total)));
        Assert.Equal(3, edits.SelectMany(e => e.To).Distinct().Count());
        _world.Realtime.Sent.Clear();

        await _world.As(_sara).DeleteAsync(comment.Id);

        var deletion = Assert.Single(_world.Events());
        Assert.Equal("deleted", deletion.Event.Action);
        Assert.Null(deletion.Event.Comment);
        // The comment first, then the replies that went with it.
        Assert.Equal(new[] { comment.Id, reply.Id }, deletion.Event.DeletedIds);
        Assert.Equal(0, deletion.Event.Total);
        Assert.Equal(new[] { _sara.Id, _omar.Id, _lina.Id }.OrderBy(id => id), deletion.To.OrderBy(id => id));
    }

    [Fact]
    public async Task Nothing_is_shown_live_for_what_was_refused()
    {
        var comment = await _world.SayAsync(_omar, _project, "Hello");
        _world.Realtime.Sent.Clear();

        await RefusedAsync(() => _world.As(_nour).AddAsync(_project.Id, new CreateGroupCommentDto { Content = "Let me in" }));
        await RefusedAsync(() => _world.As(_omar).AddAsync(_project.Id, new CreateGroupCommentDto { Content = " " }));
        await RefusedAsync(() => _world.As(_lina).UpdateAsync(comment.Id, "Changed"));
        await RefusedAsync(() => _world.As(_lina).DeleteAsync(comment.Id));

        Assert.Empty(_world.Events());
        Assert.Empty(_world.Notifications.Sent);
    }
}
