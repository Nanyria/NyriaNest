using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NyriaNest.Api.Auth;
using NyriaNest.Application.Loans;

namespace NyriaNest.Api.Controllers.Loans
{
    // The logged-in user's own reservations, taken from the token – never from the URL or
    // the body. A book's whole queue (admins) is GET api/Book/{bookId}/reservations.
    [Authorize]
    [ApiController]
    [Route("api/UserActions")]
    public class ReservationController : ControllerBase
    {
        private readonly IReservationService _reservations;

        public ReservationController(IReservationService reservations)
        {
            _reservations = reservations;
        }

        // Reserve methods

        [HttpGet("reserved")]
        public async Task<IActionResult> GetReservedBooks()
        {
            var response = await _reservations.GetReservedBooksAsync(User.GetUserId());
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpPut("reserve/{bookId}")]
        public async Task<IActionResult> ReserveBook([FromRoute] int bookId)
        {
            var response = await _reservations.ReserveBookAsync(User.GetUserId(), bookId);
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpPut("unreserve/{bookId}")]
        public async Task<IActionResult> UnreserveBook([FromRoute] int bookId)
        {
            var response = await _reservations.CancelReservationAsync(User.GetUserId(), bookId);
            return StatusCode((int)response.StatusCode, response);
        }
    }
}
