using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NyriaNest.Api.Auth;
using NyriaNest.Application.DTOs.Users;
using NyriaNest.Application.Users;
using NyriaNest.Domain.Constants;

namespace NyriaNest.Api.Controllers.Users
{
    [Authorize(Roles = Roles.SuperAdmin + "," + Roles.Admin)]
    [ApiController]
    [Route("api/[controller]")]
    public class UserController : ControllerBase
    {
        private readonly IUserAdminService _users;

        public UserController(IUserAdminService users)
        {
            _users = users;
        }

        // The admin making the request, from the token. What they may do to whom is
        // decided in UserAdminService (AdminMayNotUpdate / AdminMayNotDelete).
        private bool IsSuperAdmin => User.IsInRole(Roles.SuperAdmin);
        [HttpGet]
        public async Task<IActionResult> GetAllUsers()
        {
            var response = await _users.GetAllUsersAsync();
            return StatusCode((int)response.StatusCode, response);
        }
        //[HttpPost]
        //public async Task<IActionResult> AddUser([FromBody] CreateUserDto createUserDto)
        //{
        //    var response = await _users.AddUserAsync(createUserDto);
        //    return StatusCode((int)response.StatusCode, response);
        //}
        [Authorize(Roles = Roles.SuperAdmin)]
        [HttpPost("create-admin")]
        public async Task<IActionResult> CreateAdminUser([FromBody] CreateAdminUserDto adminUserDto)
        {
            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var response = await _users.CreateAdminUserAsync(adminUserDto);
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpDelete("{userId}")]
        public async Task<IActionResult> DeleteUser([FromRoute] string userId)
        {
            var response = await _users.DeleteUserAsync(IsSuperAdmin, userId);
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpPut("updateAsAdmin/{userId}")]
        public async Task<IActionResult> UpdateUserAsAdmin([FromRoute] string userId, [FromBody] UpdateUserAsAdminDto updateUserDto)
        {
            var response = await _users.UpdateUserAsAdminAsync(User.GetUserId(), IsSuperAdmin, userId, updateUserDto);
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpGet("{userId}")]
        public async Task<IActionResult> GetUserById([FromRoute] string userId)
        {
            var response = await _users.GetUserByIdAsync(userId);
            return StatusCode((int)response.StatusCode, response);
        }


        [HttpGet("history/{userId}")]
        public async Task<IActionResult> GetUserHistory([FromRoute] string userId)
        {
            var response = await _users.GetUserHistoryAsync(userId);
            return StatusCode((int)response.StatusCode, response);
        }
    }
}
