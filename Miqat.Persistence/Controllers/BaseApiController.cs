using Microsoft.AspNetCore.Mvc;
using Miqat.Application.Common;

namespace Miqat.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BaseApiController : ControllerBase
    {
        protected IActionResult HandleResponse<T>(T date , string Message = null)
        {
           var response = new ApiResponse<T>(date, Message);
            return Ok(response);
        }
        protected IActionResult HandleError(string message, List<string> errors = null!)
        {
            var response = new ApiResponse<string>(message, errors);
            return BadRequest(response);
        }
    }
}
