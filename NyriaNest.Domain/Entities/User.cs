using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace NyriaNest.Domain.Entities
{
    public class User : IdentityUser
    {

        [Required]
        [MaxLength(100)]

        public required string FirstName { get; set; }
        [Required]
        [MaxLength(100)]
        public required string LastName { get; set; }
        public string? ProfilePictureUrl { get; set; } // or ProfilePictureName
        [MaxLength(500)]
        public string? Bio { get; set; }
        [Required]
        [MaxLength(50)]
        public required string Role { get; set; }

        public List<FavoriteItem> ReadList { get; set; } = new List<FavoriteItem>();
        public List<ReviewItem> Reviews { get; set; } = new List<ReviewItem>();
        public List<CheckedOutItem> CheckedOutBooks { get; set; } = new List<CheckedOutItem>();
        public List<ReservationItem> ReservedBooks { get; set; } = new List<ReservationItem>();
        public List<StatusHistoryItem> UserHistory { get; set; } = new List<StatusHistoryItem>();
        public List<NotificationItem> Notifications { get; set; } = new();

    }
}
