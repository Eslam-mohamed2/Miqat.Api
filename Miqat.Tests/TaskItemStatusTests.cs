using Miqat.Application.Common;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;
using TaskStatus = Miqat.Domain.Enumerations.TaskStatus;

namespace Miqat.Tests;

/// <summary>
/// A task remembers when it was finished, and "overdue" means the same thing
/// everywhere it is reported.
/// </summary>
public class TaskItemStatusTests
{
    private static TaskItem NewTask(DateTime? dueDate = null) =>
        new("Task", "desc", Guid.NewGuid(), Priority.Medium, dueDate);

    [Fact]
    public void A_new_task_has_no_completion_time()
    {
        Assert.Null(NewTask().CompletedAt);
    }

    [Fact]
    public void Completing_a_task_records_when()
    {
        var task = NewTask();
        var before = DateTime.UtcNow;

        task.ChangeStatus(TaskStatus.Completed);

        Assert.Equal(TaskStatus.Completed, task.Status);
        Assert.NotNull(task.CompletedAt);
        Assert.InRange(task.CompletedAt!.Value, before, DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void Saving_a_completed_task_again_keeps_the_original_time()
    {
        var task = NewTask();
        task.ChangeStatus(TaskStatus.Completed);
        var first = task.CompletedAt;

        task.ChangeStatus(TaskStatus.Completed);

        Assert.Equal(first, task.CompletedAt);
    }

    [Theory]
    [InlineData(TaskStatus.Pending)]
    [InlineData(TaskStatus.In_progress)]
    [InlineData(TaskStatus.On_hold)]
    [InlineData(TaskStatus.Cancelled)]
    public void Reopening_a_task_clears_the_completion_time(TaskStatus reopenedAs)
    {
        var task = NewTask();
        task.ChangeStatus(TaskStatus.Completed);

        task.ChangeStatus(reopenedAs);

        Assert.Equal(reopenedAs, task.Status);
        Assert.Null(task.CompletedAt);
    }

    [Fact]
    public void An_open_task_past_its_due_date_is_overdue()
    {
        var task = NewTask(DateTime.UtcNow.AddHours(-1));
        Assert.True(TaskMapper.IsOverdue(task));
    }

    [Theory]
    [InlineData(TaskStatus.Completed)]
    [InlineData(TaskStatus.Cancelled)]
    public void A_finished_or_cancelled_task_is_never_overdue(TaskStatus status)
    {
        var task = NewTask(DateTime.UtcNow.AddDays(-3));
        task.ChangeStatus(status);
        Assert.False(TaskMapper.IsOverdue(task));
    }

    [Fact]
    public void A_task_without_a_due_date_or_due_later_is_not_overdue()
    {
        Assert.False(TaskMapper.IsOverdue(NewTask()));
        Assert.False(TaskMapper.IsOverdue(NewTask(DateTime.UtcNow.AddHours(1))));
    }

    [Fact]
    public void The_mapper_sends_start_and_completion_times()
    {
        var task = NewTask(DateTime.UtcNow.AddDays(2));
        task.StartDate = DateTime.UtcNow.AddDays(1);
        task.ChangeStatus(TaskStatus.Completed);

        var dto = new TaskMapper().MapToDto(task);

        Assert.Equal(task.StartDate, dto.StartDate);
        Assert.Equal(task.CompletedAt, dto.CompletedAt);
        Assert.Equal("Completed", dto.Status);
        Assert.False(dto.IsOverdue);
        // No navigations are loaded on a fresh entity; the mapper must not throw.
        Assert.Null(dto.OwnerName);
        Assert.Null(dto.GroupColor);
    }
}
