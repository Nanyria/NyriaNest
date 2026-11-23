using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NyriaNest.Api.Auth;
using NyriaNest.Application.DTOs.Friends;
using NyriaNest.Application.Friends;
using NyriaNest.Domain.Enums;

namespace NyriaNest.Api.Controllers.Friends
{
    // The logged-in user's friends and requests (#84). Who "I" am comes from the token, never from
    // the URL or the body; a friendship or request that isn't mine to change looks missing (404).
    [Authorize]
    [ApiController]
    [Route("api/friends")]
    public class FriendsController : ControllerBase
    {
        private readonly IFriendshipService _friends;

        public FriendsController(IFriendshipService friends)
        {
            _friends = friends;
        }

        // My friends, requests sent and received, and "7 of 10" for each limit
        [HttpGet]
        public async Task<IActionResult> GetOverview()
        {
            var response = await _friends.GetOverviewAsync(User.GetUserId());
            return StatusCode((int)response.StatusCode, response);
        }

        // ?userName=an finds user names starting with "an"
        [HttpGet("search")]
        public async Task<IActionResult> Search([FromQuery] string userName = "")
        {
            var response = await _friends.SearchUsersAsync(User.GetUserId(), userName);
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpPost("requests")]
        public async Task<IActionResult> SendRequest([FromBody] SendFriendRequestDto request)
        {
            var response = await _friends.SendRequestAsync(User.GetUserId(), request.UserName);
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpPut("requests/{id:int}/accept")]
        public async Task<IActionResult> Accept([FromRoute] int id)
        {
            var response = await _friends.AcceptAsync(User.GetUserId(), id);
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpPut("requests/{id:int}/decline")]
        public async Task<IActionResult> Decline([FromRoute] int id)
        {
            var response = await _friends.DeclineAsync(User.GetUserId(), id);
            return StatusCode((int)response.StatusCode, response);
        }

        // The requester takes back a pending request
        [HttpDelete("requests/{id:int}")]
        public async Task<IActionResult> Cancel([FromRoute] int id)
        {
            var response = await _friends.CancelAsync(User.GetUserId(), id);
            return StatusCode((int)response.StatusCode, response);
        }

        // Either friend ends the friendship; both directions stop sharing at once
        [HttpDelete("{id:int}")]
        public async Task<IActionResult> Remove([FromRoute] int id)
        {
            var response = await _friends.RemoveAsync(User.GetUserId(), id);
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpPut("{id:int}/sharing/physical-library")]
        public Task<IActionResult> SetPhysicalLibrarySharing([FromRoute] int id, [FromBody] SetSharingDto request) =>
            SetSharing(id, SharingSetting.PhysicalLibrary, request.On);

        [HttpPut("{id:int}/sharing/ebooks")]
        public Task<IActionResult> SetEbookSharing([FromRoute] int id, [FromBody] SetSharingDto request) =>
            SetSharing(id, SharingSetting.Ebooks, request.On);

        private async Task<IActionResult> SetSharing(int id, SharingSetting setting, bool on)
        {
            var response = await _friends.SetSharingAsync(User.GetUserId(), id, setting, on);
            return StatusCode((int)response.StatusCode, response);
        }
    }
}
