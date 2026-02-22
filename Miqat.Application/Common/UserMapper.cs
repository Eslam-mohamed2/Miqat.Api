using Riok.Mapperly.Abstractions;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;

namespace Miqat.Application.Common;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.None)]
public partial class UserMapper
{
    public partial UserDto MapUserToDto(User user);
    public partial IEnumerable<UserDto> MapUsersToDtos(IEnumerable<User> users);
    private string MapToString(object value) => value?.ToString() ?? string.Empty;
}