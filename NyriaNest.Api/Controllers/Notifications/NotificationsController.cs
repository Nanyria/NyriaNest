using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NyriaNest.Api.Auth;
using NyriaNest.Application.Notifications;

namespace NyriaNest.Api.Controllers.Notifications
{
    // The logged-in user's own notifications (#91), taken from the token – never from the URL or the body.
    [Authorize]
    [ApiController]
    [Route("api/notifications")]
    public class NotificationsController : ControllerBase
    {
        private readonly INotificationService _notifications;

        public NotificationsController(INotificationService notifications)
        {
            _notifications = notifications;
        }

        // Newest first. The bell's dropdown asks for ?unreadOnly=true&pageSize=3.
        [HttpGet]
        public async Task<IActionResult> GetMyNotifications(
            [FromQuery] bool unreadOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 10)
        {
            var response = await _notifications.GetMyNotificationsAsync(User.GetUserId(), unreadOnly, page, pageSize);
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpGet("unread-count")]
        public async Task<IActionResult> GetUnreadCount()
        {
            var response = await _notifications.GetUnreadCountAsync(User.GetUserId());
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpPut("read-all")]
        public async Task<IActionResult> MarkAllAsRead()
        {
            var response = await _notifications.MarkAllAsReadAsync(User.GetUserId());
            return StatusCode((int)response.StatusCode, response);
        }

        [HttpPut("{id:int}/read")]
        public async Task<IActionResult> MarkAsRead([FromRoute] int id)
        {
            var response = await _notifications.MarkAsReadAsync(User.GetUserId(), id);
            return StatusCode((int)response.StatusCode, response);
        }
    }
}
