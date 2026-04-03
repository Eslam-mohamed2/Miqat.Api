using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Miqat.Application.Common;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.None)]
public partial class GroupMapper
{
    [MapperIgnoreTarget(nameof(GroupDto.OwnerName))]
    [MapperIgnoreTarget(nameof(GroupDto.MemberCount))]
    [MapperIgnoreTarget(nameof(GroupDto.TaskCount))]
    private partial GroupDto MapToDtoInternal(Group group);

    public GroupDto MapToDto(Group group)
    {
        var dto = MapToDtoInternal(group);
        dto.OwnerName = group.Owner?.FullName;
        return dto;
    }

    public IEnumerable<GroupDto> MapToDtos(IEnumerable<Group> groups)
        => groups.Select(MapToDto);
}