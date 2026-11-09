using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NyriaNest.Api.Auth;
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

        public LoanController(ILoanService loans)
        {
            _loans = loans;
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
    }
}
