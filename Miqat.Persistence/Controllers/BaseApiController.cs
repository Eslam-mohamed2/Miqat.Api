using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Miqat.API.Controllers
{
    /// <summary>
    /// Base of the calendar-module controllers: signed-in callers only, and the
    /// two answers nearly every action ends with, written once.
    /// </summary>
    [ApiController]
    [Authorize]
    [Route("api/[controller]")]
    public abstract class BaseApiController : ControllerBase
    {
        /// <summary>200 with the value, or 404 when the service found nothing (or nothing the caller may know about).</summary>
        protected IActionResult OkOrNotFound<T>(T? value, string what) where T : class =>
            value == null ? NotFound(Missing(what)) : Ok(value);

        /// <summary>204, or 404 when there was nothing to act on.</summary>
        protected IActionResult NoContentOrNotFound(bool found, string what) =>
            found ? NoContent() : NotFound(Missing(what));

        private static object Missing(string what) => new { message = $"{what} not found." };
    }
}
