using Miqat.Application.Common;
using Miqat.Application.Services;
using Miqat.Domain.Entities;

namespace Miqat.Tests;

/// <summary>
/// The authorization boundary.
///
/// Before this policy existed, every service loaded rows by id and acted on them,
/// so any authenticated user could read, edit and delete anyone else's tasks and
/// projects. These lock that shut: each test names the person, the thing, and the
/// answer.
/// </summary>
public class AccessPolicyTests
{
    private static readonly Guid Owner = Guid.NewGuid();
    private static readonly Guid Member = Guid.NewGuid();
    private static readonly Guid Outsider = Guid.NewGuid();
    private static readonly Guid Assignee = Guid.NewGuid();

    private static readonly Guid GroupId = Guid.NewGuid();
    private static readonly Guid TaskId = Guid.NewGuid();

    /// <param name="taskOwner">Who created the task.</param>
    /// <param name="taskGroup">The project it belongs to, if any.</param>
    private static AccessPolicy PolicyFor(
        Guid? caller,
        string role = "User",
        Guid? taskOwner = null,
        Guid? taskGroup = null,
        Guid? assignedTo = null)
    {
        var group = new Group("Website", "desc", Owner, "#2ec4a0");
        SetId(group, GroupId);

        var task = new TaskItem(
            "Task", "desc", taskOwner ?? Owner,
            Domain.Enumerations.Priority.Low, null,
            assignedTo, taskGroup, null,
            Domain.Enumerations.RecurrencePattern.None, null);
        SetId(task, TaskId);

        var uow = new FakeUnitOfWork();
        uow.Register<Group>(new FakeRepository<Group>(new List<Group> { group }, g => g.Id));
        uow.Register<TaskItem>(new FakeRepository<TaskItem>(new List<TaskItem> { task }, t => t.Id));
        uow.Register<GroupMember>(new FakeRepository<GroupMember>(
            new List<GroupMember> { new(GroupId, Member) }, m => m.Id));

        return new AccessPolicy(uow, new FakeCurrentUser(caller, role));
    }

    private static void SetId(object entity, Guid id) =>
        entity.GetType().BaseType!.GetProperty("Id")!.SetValue(entity, id);

    // ── Projects ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Owner_can_view_and_manage_their_project()
    {
        var policy = PolicyFor(Owner);
        Assert.True(await policy.CanViewGroupAsync(GroupId));
        Assert.True(await policy.CanManageGroupAsync(GroupId));
    }

    [Fact]
    public async Task Member_can_view_but_not_manage_the_project()
    {
        var policy = PolicyFor(Member);
        Assert.True(await policy.CanViewGroupAsync(GroupId));
        Assert.False(await policy.CanManageGroupAsync(GroupId));
    }

    [Fact]
    public async Task Outsider_can_neither_view_nor_manage_the_project()
    {
        var policy = PolicyFor(Outsider);
        Assert.False(await policy.CanViewGroupAsync(GroupId));
        Assert.False(await policy.CanManageGroupAsync(GroupId));
    }

    // ── Tasks ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Creator_can_view_edit_and_delete_their_own_task()
    {
        var policy = PolicyFor(Owner, taskOwner: Owner);
        Assert.True(await policy.CanViewTaskAsync(TaskId));
        Assert.True(await policy.CanEditTaskAsync(TaskId));
        Assert.True(await policy.CanDeleteTaskAsync(TaskId));
    }

    [Fact]
    public async Task Outsider_cannot_touch_a_personal_task()
    {
        var policy = PolicyFor(Outsider, taskOwner: Owner);
        Assert.False(await policy.CanViewTaskAsync(TaskId));
        Assert.False(await policy.CanEditTaskAsync(TaskId));
        Assert.False(await policy.CanDeleteTaskAsync(TaskId));
    }

    [Fact]
    public async Task Assignee_can_see_and_edit_a_task_assigned_to_them()
    {
        var policy = PolicyFor(Assignee, taskOwner: Owner, assignedTo: Assignee);
        Assert.True(await policy.CanViewTaskAsync(TaskId));
        Assert.True(await policy.CanEditTaskAsync(TaskId));
    }

    [Fact]
    public async Task Project_member_can_edit_shared_work_but_not_delete_it()
    {
        // The distinction that matters on a team board: collaborators move work
        // forward, they do not destroy someone else's.
        var policy = PolicyFor(Member, taskOwner: Owner, taskGroup: GroupId);
        Assert.True(await policy.CanViewTaskAsync(TaskId));
        Assert.True(await policy.CanEditTaskAsync(TaskId));
        Assert.False(await policy.CanDeleteTaskAsync(TaskId));
    }

    [Fact]
    public async Task Project_owner_can_delete_work_inside_their_project()
    {
        var policy = PolicyFor(Owner, taskOwner: Member, taskGroup: GroupId);
        Assert.True(await policy.CanDeleteTaskAsync(TaskId));
    }

    [Fact]
    public async Task Outsider_cannot_reach_a_task_through_its_project()
    {
        var policy = PolicyFor(Outsider, taskOwner: Owner, taskGroup: GroupId);
        Assert.False(await policy.CanViewTaskAsync(TaskId));
    }

    // ── Admin + guards ───────────────────────────────────────────────────────

    [Fact]
    public async Task Admin_bypasses_ownership()
    {
        var policy = PolicyFor(Outsider, role: "Admin", taskOwner: Owner);
        Assert.True(await policy.CanViewTaskAsync(TaskId));
        Assert.True(await policy.CanManageGroupAsync(GroupId));
    }

    [Fact]
    public async Task Anonymous_caller_is_rejected_rather_than_allowed()
    {
        var policy = PolicyFor(caller: null);
        await Assert.ThrowsAsync<ApiException>(() => policy.CanViewGroupAsync(GroupId));
    }

    [Fact]
    public async Task Missing_rows_are_denied_not_granted()
    {
        var policy = PolicyFor(Owner);
        Assert.False(await policy.CanViewGroupAsync(Guid.NewGuid()));
        Assert.False(await policy.CanViewTaskAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task RequireAsync_raises_403_when_the_check_fails()
    {
        var policy = PolicyFor(Outsider);
        var error = await Assert.ThrowsAsync<ApiException>(() =>
            policy.RequireAsync(policy.CanViewGroupAsync(GroupId), "nope"));

        Assert.Equal(403, error.StatusCode);
    }
}
