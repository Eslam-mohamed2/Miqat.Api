using Miqat.Application.Common;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;
using Miqat.Domain.Entities;

namespace Miqat.Application.Services
{
    /// <inheritdoc />
    public class DeviceService : IDeviceService
    {
        private const int MaxDevices = 10;

        private readonly IUnitOfWork _unitOfWork;
        private readonly ICurrentUserService _currentUser;
        private readonly IPushSender _push;

        public DeviceService(IUnitOfWork unitOfWork, ICurrentUserService currentUser, IPushSender push)
        {
            _unitOfWork = unitOfWork;
            _currentUser = currentUser;
            _push = push;
        }

        private IGenericRepository<Device> Devices => _unitOfWork.Repository<Device>();

        public PushKeyDto GetPushKey() =>
            new() { IsConfigured = _push.IsConfigured, PublicKey = _push.PublicKey };

        public async Task<List<DeviceDto>> GetMineAsync()
        {
            var userId = _currentUser.RequireUserId();
            var devices = await Devices.FindAsync(d => d.UserId == userId);

            return devices.OrderByDescending(d => d.LastSeenAt).Select(ToDto).ToList();
        }

        public async Task<DeviceDto> RegisterAsync(RegisterDeviceDto dto)
        {
            var userId = _currentUser.RequireUserId();
            var endpoint = dto.Endpoint.Trim();

            // The server will post to this address, so it has to be the push
            // service of a real browser and nothing else.
            if (!_push.IsAllowedEndpoint(endpoint))
                throw new ApiException("That is not the push address of a supported browser.", 400);

            var device = (await Devices.FindAsync(d => d.Endpoint == endpoint)).FirstOrDefault();

            if (device != null)
            {
                // The same browser again - possibly after another person signed in on it.
                device.Register(userId, dto.P256dh.Trim(), dto.Auth.Trim(), dto.Name);
                device.SetUpdated(userId.ToString());
            }
            else
            {
                await ForgetOldestAsync(userId);

                device = new Device(userId, endpoint, dto.P256dh.Trim(), dto.Auth.Trim(), dto.Name);
                await Devices.AddAsync(device);
            }

            await _unitOfWork.CompleteAsync();
            return ToDto(device);
        }

        public async Task<bool> RemoveAsync(Guid deviceId)
        {
            var userId = _currentUser.RequireUserId();

            var device = await Devices.GetByIdAsync(deviceId);
            if (device == null || device.UserId != userId) return false;

            Devices.Delete(device);
            await _unitOfWork.CompleteAsync();
            return true;
        }

        /// <summary>Keeps the list bounded: a new device replaces the ones that have not been seen for the longest.</summary>
        private async Task ForgetOldestAsync(Guid userId)
        {
            var mine = (await Devices.FindAsync(d => d.UserId == userId))
                .OrderByDescending(d => d.LastSeenAt)
                .ToList();

            Devices.DeleteRange(mine.Skip(MaxDevices - 1));
        }

        private static DeviceDto ToDto(Device device) => new()
        {
            Id = device.Id,
            Name = device.Name,
            LastSeenAt = device.LastSeenAt,
            CreatedAt = device.CreatedAt
        };
    }
}
