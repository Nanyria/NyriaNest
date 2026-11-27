using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using NyriaNest.Api.Auth;
using NyriaNest.Application.DTOs.Books;
using NyriaNest.Application.Loans;

namespace NyriaNest.Api.Controllers.Loans
{
    // The logged-in user's own loans, taken from the token – never from the URL or the body.
    [Authorize]
    [ApiController]
    [Route("api/UserActions")]
    public class LoanController : ControllerBase
    {
        private readonly ILoanService _loans;
        private readonly ILendingService _lending;

        public LoanController(ILoanService loans, ILendingService lending)
        {
            _loans = loans;
            _lending = lending;
        }

        // Borrow methods

        [HttpGet("checkedout")]
        public async Task<IActionResult> GetCheckedOutBooks()
        {
            var response = await _loans.GetCheckedOutBooksAsync(User.GetUserId());
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpPut("borrow/{bookId}")]
        public async Task<IActionResult> BorrowBook([FromRoute] int bookId)
        {
            var response = await _loans.CheckOutBookAsync(User.GetUserId(), bookId);
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpPut("return/{bookId}")]
        public async Task<IActionResult> ReturnBook([FromRoute] int bookId)
        {
            var response = await _loans.ReturnBookAsync(User.GetUserId(), bookId);
            return StatusCode((int)response.StatusCode, response);
        }

        // #85: a friend's book. I say I returned it, and when; the owner confirms through
        // api/my/books/{id}/return. A book that isn't lent to me looks missing (404).
        // The body is optional: without a date it was returned today.
        [HttpPut("report-returned/{bookId}")]
        public async Task<IActionResult> ReportReturned([FromRoute] int bookId,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ReturnBookRequest? body)
        {
            var response = await _lending.ReportReturnedAsync(bookId, User.GetUserId(), body ?? new ReturnBookRequest(null));
            return StatusCode((int)response.StatusCode, response);
        }
    }
}
