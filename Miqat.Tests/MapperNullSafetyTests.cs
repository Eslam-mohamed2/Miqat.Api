using Miqat.Application.Common;
using Miqat.Domain.Entities;
using Miqat.Domain.Enumerations;

namespace Miqat.Tests;

/// <summary>
/// Mappers must work on rows whose related rows were not loaded.
///
/// Most queries load a project, task or notification without its owner /
/// assignee / triggering user. A mapper that dereferences those blindly turns
/// every such response into a 500 - which has happened here twice, once for
/// tasks and once for projects.
/// </summary>
public class MapperNullSafetyTests
{
    private static readonly Guid SomeUser = Guid.NewGuid();

    [Fact]
    public void Project_without_its_owner_loaded_maps_without_throwing()
    {
        var group = new Group("Website", "desc", SomeUser, "#2ec4a0");

        var dto = new GroupMapper().MapToDto(group);

        Assert.Equal(group.Id, dto.Id);
        Assert.Equal("Website", dto.Name);
        Assert.Equal(SomeUser, dto.OwnerId);
        Assert.Null(dto.OwnerName);
        Assert.Null(dto.OwnerProfilePictureUrl);
        Assert.Equal(0, dto.MemberCount);
        Assert.Equal(0, dto.TaskCount);
    }

    [Fact]
    public void Project_with_its_owner_loaded_carries_name_and_picture()
    {
        var owner = new User("Sara Ali", "sara@example.com", DateTime.UtcNow.AddYears(-30), null, null)
        {
            ProfilePictureUrl = "https://example.com/sara.png"
        };
        var group = new Group("Website", null, owner.Id) { Owner = owner };

        var dto = new GroupMapper().MapToDto(group);

        Assert.Equal("Sara Ali", dto.OwnerName);
        Assert.Equal("https://example.com/sara.png", dto.OwnerProfilePictureUrl);
    }

    [Fact]
    public void System_notification_without_a_triggering_user_maps_without_throwing()
    {
        var notification = new Notification(
            "Task due soon", "\"Report\" is due in about 3 hours.",
            NotificationType.TaskDueSoon, SomeUser,
            triggeredByUserId: null, linkedEntityId: Guid.NewGuid(), linkedEntityType: "TaskItem");

        var dto = new NotificationMapper().MapToDto(notification);

        Assert.Equal("TaskDueSoon", dto.Type);
        Assert.Null(dto.TriggeredByUserId);
        Assert.Null(dto.TriggeredByUserName);
        Assert.Null(dto.TriggeredByUserProfilePictureUrl);
        Assert.Equal("TaskItem", dto.LinkedEntityType);
    }

    [Fact]
    public void Task_without_owner_assignee_or_project_loaded_maps_without_throwing()
    {
        var task = new TaskItem("Task", null, SomeUser, Priority.High, null, Guid.NewGuid(), Guid.NewGuid());

        var dto = new TaskMapper().MapToDto(task);

        Assert.Equal("High", dto.Priority);
        Assert.Null(dto.OwnerName);
        Assert.Null(dto.AssignedToUserName);
        Assert.Null(dto.AssignedToUserProfilePictureUrl);
        Assert.Null(dto.GroupName);
        Assert.Null(dto.GroupColor);
    }
}
