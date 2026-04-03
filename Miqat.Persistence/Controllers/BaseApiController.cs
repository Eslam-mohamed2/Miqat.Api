using Microsoft.AspNetCore.Mvc;
using Miqat.Application.Common;

namespace Miqat.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BaseApiController : ControllerBase
    {
        protected IActionResult HandleResponse<T>(T data, string? message = null)
        {
            var response = ApiResponse<T>.Ok(data, message);
            return Ok(response);
        }

        protected IActionResult HandleError(string message, List<string>? errors = null)
        {
            var response = ApiResponse<string>.Fail(message, errors);
            return BadRequest(response);
        }
    }
}