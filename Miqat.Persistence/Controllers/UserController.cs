using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Miqat.Application.Interfaces;
using Miqat.Application.Modules;

namespace Miqat.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : BaseApiController
    {
        private readonly IUserService _userService;
        public UserController(IUserService userService)
        {
            _userService = userService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _userService.GetAllUsers();
            return HandleResponse(users);
        }
        [HttpGet("{id}")]
        public async Task<IActionResult> GetUserById(Guid id)
        {
            var user = await _userService.GetUserById(id);
            if (user == null) return HandleError("User not found");
            return HandleResponse(user);
        }

        [HttpPost]
        public async Task<IActionResult> CreateUser(UserDto userDto)
        {
            var createdUser = await _userService.CreateAsync(userDto);
            return HandleResponse(createdUser, "User created successfully");
        }
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(Guid id, UserDto userDto)
        {
            var isUpdated = await _userService.UpdateAsync(id, userDto);
            if (!isUpdated) return HandleError("User not found or update failed");
            return HandleResponse(isUpdated, "User updated successfully");
        }
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            var isDeleted = await _userService.DeleteAsync(id);
            if (!isDeleted) return HandleError("User not found or delete failed");
            return HandleResponse(isDeleted, "User deleted successfully");
        }
    }
}
