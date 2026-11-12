using Miqat.Application.Modules;

namespace Miqat.Application.Interfaces
{
    /// <summary>The browsers of the caller that receive notifications while the app is closed.</summary>
    public interface IDeviceService
    {
        PushKeyDto GetPushKey();
        Task<List<DeviceDto>> GetMineAsync();

        /// <summary>Registers a browser. Registering the same browser again updates it.</summary>
        Task<DeviceDto> RegisterAsync(RegisterDeviceDto dto);

        Task<bool> RemoveAsync(Guid deviceId);
    }
}
