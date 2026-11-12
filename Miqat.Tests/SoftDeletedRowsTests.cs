using Miqat.Application.Services;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Tests;

/// <summary>
/// A deleted project or task is gone as far as permissions are concerned.
///
/// Rows are soft-deleted, so they are still found by id. Without this rule the
/// owner of a deleted project could keep adding members to it, and a deleted
/// task could be "deleted" again or have its comments read.
/// </summary>
public class SoftDeletedRowsTests
{
    private static readonly Guid Owner = Guid.NewGuid();

    private static (AccessPolicy Policy, Group Group, TaskItem Task) Build(string role = "User")
    {
        var group = new Group("Website", "desc", Owner, "#2ec4a0");
        var task = new TaskItem("Task", "desc", Owner, Priority.Low, null, null, group.Id);

        var uow = new FakeUnitOfWork();
        uow.Register<Group>(new FakeRepository<Group>(new List<Group> { group }, g => g.Id));
        uow.Register<TaskItem>(new FakeRepository<TaskItem>(new List<TaskItem> { task }, t => t.Id));
        uow.Register<GroupMember>(new FakeRepository<GroupMember>(new List<GroupMember>(), m => m.Id));

        return (new AccessPolicy(uow, new FakeCurrentUser(Owner, role)), group, task);
    }

    [Fact]
    public async Task Owner_loses_access_to_a_deleted_project()
    {
        var (policy, group, _) = Build();
        Assert.True(await policy.CanManageGroupAsync(group.Id));

        group.SoftDelete();

        Assert.False(await policy.CanViewGroupAsync(group.Id));
        Assert.False(await policy.CanManageGroupAsync(group.Id));
    }

    [Fact]
    public async Task Creator_loses_access_to_a_deleted_task()
    {
        var (policy, _, task) = Build();
        Assert.True(await policy.CanDeleteTaskAsync(task.Id));

        task.SoftDelete();

        Assert.False(await policy.CanViewTaskAsync(task.Id));
        Assert.False(await policy.CanEditTaskAsync(task.Id));
        Assert.False(await policy.CanDeleteTaskAsync(task.Id));
    }

    [Fact]
    public async Task Admin_is_not_granted_access_to_deleted_rows_either()
    {
        var (policy, group, task) = Build(role: "Admin");

        group.SoftDelete();
        task.SoftDelete();

        Assert.False(await policy.CanViewGroupAsync(group.Id));
        Assert.False(await policy.CanManageGroupAsync(group.Id));
        Assert.False(await policy.CanViewTaskAsync(task.Id));
        Assert.False(await policy.CanDeleteTaskAsync(task.Id));
    }

    [Fact]
    public async Task A_task_stays_visible_to_its_creator_when_only_its_project_is_deleted()
    {
        var (policy, group, task) = Build();

        group.SoftDelete();

        // The creator's own task is still theirs; only the project route is closed.
        Assert.True(await policy.CanViewTaskAsync(task.Id));
        Assert.True(await policy.CanDeleteTaskAsync(task.Id));
    }
}
