using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Miqat.Application.Common;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.None)]
public partial class UserMapper
{
    [MapProperty(nameof(User.Gender), nameof(UserDto.Gender))]
    [MapProperty(nameof(User.Role), nameof(UserDto.Role))]
    public partial UserDto MapUserToDto(User user);

    public partial IEnumerable<UserDto> MapUsersToDtos(IEnumerable<User> users);

    private string MapEnumToString(Miqat.Domain.Enumerations.UserRole role) => role.ToString();
    private string? MapNullableEnumToString(Miqat.Domain.Enumerations.Gender? gender) => gender?.ToString();
}