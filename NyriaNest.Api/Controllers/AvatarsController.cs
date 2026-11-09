using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NyriaNest.Application.Accounts;

namespace NyriaNest.Api.Controllers
{
    /// <summary>
    /// Serves uploaded profile pictures. Public, because an img tag can't send the
    /// login token, and profile pictures are meant to be seen by other readers.
    /// </summary>
    [AllowAnonymous]
    [ApiController]
    [Route("api/avatars")]
    public class AvatarsController : ControllerBase
    {
        private readonly IProfilePictureService _profilePictures;

        public AvatarsController(IProfilePictureService profilePictures)
        {
            _profilePictures = profilePictures;
        }

        [HttpGet("{userId}/{fileName}")]
        public async Task<IActionResult> Get(string userId, string fileName, CancellationToken ct)
        {
            var picture = await _profilePictures.OpenAsync(userId, fileName, ct);
            if (picture is null)
                return NotFound();

            // Every upload gets a new name, so a URL always means the same picture
            Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            // Never let the browser guess another type than the one we checked
            Response.Headers.XContentTypeOptions = "nosniff";
            return File(picture.Content, picture.ContentType);
        }
    }
}
