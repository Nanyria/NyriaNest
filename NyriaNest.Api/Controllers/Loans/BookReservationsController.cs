using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NyriaNest.Application.Loans;
using NyriaNest.Domain.Constants;

namespace NyriaNest.Api.Controllers.Loans
{
    // A book's reservation queue and its borrow requests, beside the other admin book endpoints in api/Book.
    [Authorize(Roles = Roles.SuperAdmin + "," + Roles.Admin)]
    [Route("api/Book")]
    [ApiController]
    public class BookReservationsController : ControllerBase
    {
        private readonly IReservationService _reservations;
        private readonly IBorrowRequestService _requests;

        public BookReservationsController(IReservationService reservations, IBorrowRequestService requests)
        {
            _reservations = reservations;
            _requests = requests;
        }

        /// <summary>Every request to borrow the book, finished ones too, newest first (admins only, #85 / #50).</summary>
        [HttpGet("{bookId:int}/borrow-requests")]
        public async Task<IActionResult> GetBookBorrowRequests(int bookId)
        {
            var response = await _requests.GetBookRequestsAsync(bookId);
            return StatusCode((int)response.StatusCode, response);
        }

        /// <summary>The whole reservation queue, oldest first, with each reader's name (admins only, #71).</summary>
        [HttpGet("{bookId:int}/reservations")]
        public async Task<IActionResult> GetBookReservations(int bookId)
        {
            var response = await _reservations.GetBookReservationsAsync(bookId);
            return StatusCode((int)response.StatusCode, response);
        }
    }
}
