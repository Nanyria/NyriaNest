using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NyriaNest.Api.Auth;
using NyriaNest.Application.DTOs.Loans;
using NyriaNest.Application.Loans;

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
    }
}
