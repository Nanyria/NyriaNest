using NyriaNest.Application.DTOs.History;
using System.ComponentModel.DataAnnotations;

namespace NyriaNest.Application.DTOs.Users
{
    public class UserDto
    {
        public string UserId { get; set; }
        public required string UserName { get; set; }
        public required string FirstName { get; set; }
        public required string LastName { get; set; }

        public required string Email { get; set; }
        // No Password: a user's password (or its hash) is never sent to a client
        public string? ProfilePictureUrl { get; set; } // or ProfilePictureName
        [MaxLength(500)]
        public string? Bio { get; set; }
        public required string Role { get; set; }
        public List<FavoriteItemDto> ReadList { get; set; } = new List<FavoriteItemDto>();
        public List<ReviewItemDto> Reviews { get; set; } = new List<ReviewItemDto>();
        public List<CheckedOutItemDto> CheckedOutBooks { get; set; } = new List<CheckedOutItemDto>();
        public List<ReservationItemDto> ReservedBooks { get; set; } = new List<ReservationItemDto>();
        public List<StatusHistoryItemDto> UserHistory { get; set; } = new List<StatusHistoryItemDto>();
        public List<NotificationItemDto> Notifications { get; set; } = new();

    }
}

