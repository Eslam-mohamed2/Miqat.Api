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
        dto.MemberCount = group.Members?.Count ?? 0;
        dto.TaskCount = group.Tasks?.Count ?? 0;
        return dto;
    }

    public IEnumerable<GroupDto> MapToDtos(IEnumerable<Group> groups)
        => groups.Select(MapToDto);
}