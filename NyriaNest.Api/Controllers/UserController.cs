using NyriaNest.Api.Helpers.Enums;
using NyriaNest.Api.Models.Users;
using NyriaNest.Api.Models.Users.UserDTOs;
using NyriaNest.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NyriaNest.Api.Controllers
{
    [Authorize(Roles = Roles.SuperAdmin + "," + Roles.Admin)]
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;

        public UserController(IUserService userService)
        {
            _userService = userService;
        }

        // The admin making the request, from the token. What they may do to whom is
        // decided in UserService (AdminMayNotUpdate / AdminMayNotDelete).
        private string CurrentUserId =>
            User.FindFirst("userId")?.Value
            ?? throw new InvalidOperationException("The token has no userId claim.");
        private bool IsSuperAdmin => User.IsInRole(Roles.SuperAdmin);
        [HttpGet]
        public async Task<IActionResult> GetAllUsers()
        {
            var response = await _userService.GetAllUsersAsync();
            return StatusCode((int)response.StatusCode, response);
        }
        //[HttpPost]
        //public async Task<IActionResult> AddUser([FromBody] CreateUserDto createUserDto)
        //{
        //    var response = await _userService.AddUserAsync(createUserDto);
        //    return StatusCode((int)response.StatusCode, response);
        //}
        [Authorize(Roles = Roles.SuperAdmin)]
        [HttpPost("create-admin")]
        public async Task<IActionResult> CreateAdminUser([FromBody] CreateAdminUserDto adminUserDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var response = await _userService.CreateAdminUserAsync(adminUserDto);
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpDelete("{userId}")]
        public async Task<IActionResult> DeleteUser([FromRoute] string userId)
        {
            var response = await _userService.DeleteUserAsync(IsSuperAdmin, userId);
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpPut("updateAsAdmin/{userId}")]
        public async Task<IActionResult> UpdateUserAsAdmin([FromRoute] string userId, [FromBody] UpdateUserAsAdminDto updateUserDto)
        {
            var response = await _userService.UpdateUserAsAdminAsync(CurrentUserId, IsSuperAdmin, userId, updateUserDto);
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetUserById([FromRoute] string userId)
        {
            var response = await _userService.GetUserByIdAsync(userId);
            return StatusCode((int)response.StatusCode, response);
        }


        [HttpGet("history/{userId}")]
        public async Task<IActionResult> GetUserHistory([FromRoute] string userId)
        {
            var response = await _userService.GetUserHistoryAsync(userId);
            return StatusCode((int)response.StatusCode, response);
        }
    }
}
