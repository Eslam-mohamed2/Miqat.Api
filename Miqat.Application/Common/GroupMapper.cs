using Miqat.Application.Modules;
using Miqat.Domain.Entities;

namespace Miqat.Application.Common;

/// <summary>
/// Group -> GroupDto, written out by hand.
///
/// The generated mapper read group.Owner.ProfilePictureUrl without a null
/// check (Owner is declared non-nullable), which threw for every project
/// loaded without its owner - that is, every list and every newly created
/// project. Each related value is read through a null-safe navigation here.
/// </summary>
public class GroupMapper
{
    public GroupDto MapToDto(Group group) => new()
    {
        Id = group.Id,
        Name = group.Name,
        Description = group.Description,
        Color = group.Color,
        OwnerId = group.OwnerId,
        OwnerName = group.Owner?.FullName,
        OwnerProfilePictureUrl = group.Owner?.ProfilePictureUrl,

        // Only meaningful when the caller loaded these navigations. GroupService
        // attaches the real counts afterwards, because the list queries do not
        // include them and every card read 0.
        MemberCount = group.Members?.Count ?? 0,
        TaskCount = group.Tasks?.Count ?? 0,

        CreatedAt = group.CreatedAt,
        UpdatedAt = group.UpdatedAt
    };

    public IEnumerable<GroupDto> MapToDtos(IEnumerable<Group> groups)
        => groups.Select(MapToDto).ToList();
}
