using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NyriaNest.Api.Auth;
using NyriaNest.Application.ReadingList;

namespace NyriaNest.Api.Controllers.ReadingList
{
    // The logged-in user's own favorites, taken from the token – never from the URL or the body.
    [Authorize]
    [ApiController]
    [Route("api/UserActions")]
    public class ReadingListController : ControllerBase
    {
        private readonly IReadingListService _readingList;

        public ReadingListController(IReadingListService readingList)
        {
            _readingList = readingList;
        }

        [HttpGet("favorites")]
        public async Task<IActionResult> GetFavoriteBooks()
        {
            var response = await _readingList.GetFavoriteItemsAsync(User.GetUserId());
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpPut("favorite/{bookId}")]
        public async Task<IActionResult> AddToFavorites([FromRoute] int bookId)
        {
            var response = await _readingList.AddToFavoritesAsync(User.GetUserId(), bookId);
            return StatusCode((int)response.StatusCode, response);
        }
        [HttpDelete("unfavorite/{bookId}")]
        public async Task<IActionResult> RemoveFromFavorites([FromRoute] int bookId)
        {
            var response = await _readingList.RemoveFromFavoritesAsync(User.GetUserId(), bookId);
            return StatusCode((int)response.StatusCode, response);
        }
    }
}
