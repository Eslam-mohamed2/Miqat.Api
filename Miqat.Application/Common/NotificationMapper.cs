using Miqat.Application.Modules;
using Miqat.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Miqat.Application.Common;

[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.None)]
public partial class NotificationMapper
{
    [MapProperty(nameof(Notification.Type), nameof(NotificationDto.Type))]
    [MapProperty([nameof(Notification.TriggeredByUser), nameof(User.FullName)], nameof(NotificationDto.TriggeredByUserName))]
    public partial NotificationDto MapToDto(Notification notification);

    public partial IEnumerable<NotificationDto> MapToDtos(IEnumerable<Notification> notifications);

    private string MapEnumToString(Miqat.Domain.Enumerations.NotificationType type) => type.ToString();
}