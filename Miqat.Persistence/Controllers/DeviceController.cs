using Microsoft.AspNetCore.Mvc;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;

namespace Miqat.API.Controllers
{
    /// <summary>
    /// The browsers of the caller that receive notifications while the app is
    /// closed (Web Push). A browser subscribes with the key from "push-key" and
    /// registers the subscription here.
    /// </summary>
    public class DeviceController : BaseApiController
    {
        private readonly IDeviceService _devices;

        public DeviceController(IDeviceService devices) => _devices = devices;

        [HttpGet]
        public async Task<IActionResult> GetMine() => Ok(await _devices.GetMineAsync());

        /// <summary>The public key a browser needs to subscribe, and whether the server can send at all.</summary>
        [HttpGet("push-key")]
        public IActionResult GetPushKey() => Ok(_devices.GetPushKey());

        /// <summary>Registers this browser. Registering it again updates it.</summary>
        [HttpPost]
        public async Task<IActionResult> Register([FromBody] RegisterDeviceDto dto)
            => Ok(await _devices.RegisterAsync(dto));

        /// <summary>Stops notifications on a device.</summary>
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Remove(Guid id)
            => NoContentOrNotFound(await _devices.RemoveAsync(id), "Device");
    }
}
