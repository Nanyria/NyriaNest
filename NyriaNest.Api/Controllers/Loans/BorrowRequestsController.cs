using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NyriaNest.Api.Auth;
using NyriaNest.Application.DTOs.Loans;
using NyriaNest.Application.Loans;
using NyriaNest.Domain.Constants;

namespace NyriaNest.Api.Controllers.Loans
{
    // Asking to borrow a friend's book, and the owner's answer (#85). Who "I" am comes from the
    // token, never from the URL or the body; a request that isn't mine to answer or withdraw looks
    // missing (404).
    [Authorize]
    [ApiController]
    [Route("api/borrow-requests")]
    public class BorrowRequestsController : ControllerBase
    {
        private readonly IBorrowRequestService _requests;

        public BorrowRequestsController(IBorrowRequestService requests)
        {
            _requests = requests;
        }

        // The owner: open requests for my books, to accept or decline
        [HttpGet("incoming")]
        public async Task<IActionResult> Incoming()
        {
            var response = await _requests.GetIncomingAsync(User.GetUserId());
            return StatusCode((int)response.StatusCode, response);
        }

        // The friend: my own requests, still waiting for an answer
        [HttpGet("mine")]
        public async Task<IActionResult> Mine()
        {
            var response = await _requests.GetMineAsync(User.GetUserId());
            return StatusCode((int)response.StatusCode, response);
        }

        // The friend asks to borrow a book
        [HttpPost]
        public async Task<IActionResult> Ask([FromBody] CreateBorrowRequestDto body)
        {
            var response = await _requests.RequestAsync(User.GetUserId(), body.BookId);
            return StatusCode((int)response.StatusCode, response);
        }

        // The owner lends the book to this friend; anyone else who asked joins the queue
        [HttpPut("{id:int}/accept")]
        public async Task<IActionResult> Accept([FromRoute] int id)
        {
            var response = await _requests.AcceptAsync(User.GetUserId(), id);
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpPut("{id:int}/decline")]
        public async Task<IActionResult> Decline([FromRoute] int id)
        {
            var response = await _requests.DeclineAsync(User.GetUserId(), id);
            return StatusCode((int)response.StatusCode, response);
        }

        // The friend withdraws their own request
        [HttpPut("{id:int}/cancel")]
        public async Task<IActionResult> Cancel([FromRoute] int id)
        {
            var response = await _requests.CancelAsync(User.GetUserId(), id);
            return StatusCode((int)response.StatusCode, response);
        }

        // Support (#50): an admin cancels a request, with a reason both the friend and the owner see
        [HttpPut("{id:int}/admin-cancel")]
        [Authorize(Roles = Roles.SuperAdmin + "," + Roles.Admin)]
        public async Task<IActionResult> AdminCancel([FromRoute] int id, [FromBody] AdminCancelBorrowRequestDto body)
        {
            var response = await _requests.AdminCancelAsync(User.GetUserId(), id, body.Reason);
            return StatusCode((int)response.StatusCode, response);
        }
    }
}
