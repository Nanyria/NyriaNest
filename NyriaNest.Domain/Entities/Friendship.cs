using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using NyriaNest.Domain.Enums;

namespace NyriaNest.Domain.Entities
{
    /// <summary>
    /// A friend request and, once accepted, the friendship (#84). One row per pair of users,
    /// whichever of them asked; the sharing settings for both directions sit on the same row.
    /// All four settings start off: accepting a request shares nothing by itself (ADR 0005).
    /// </summary>
    public class Friendship
    {
        [Key]
        public int Id { get; set; }

        /// <summary>The user who sent the request.</summary>
        [Required]
        public string RequesterId { get; set; } = null!;

        [ForeignKey("RequesterId")]
        public User Requester { get; set; } = null!;

        /// <summary>The user who was asked.</summary>
        [Required]
        public string AddresseeId { get; set; } = null!;

        [ForeignKey("AddresseeId")]
        public User Addressee { get; set; } = null!;

        public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;

        /// <summary>When the request was sent; set again when asking anew after a decline.</summary>
        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        /// <summary>When the addressee accepted ("Friends since").</summary>
        public DateTime? AcceptedAt { get; set; }

        /// <summary>When the addressee declined; cleared when the requester asks again.</summary>
        public DateTime? DeclinedAt { get; set; }

        // ---- What the requester shares with the addressee ----

        public bool RequesterSharesPhysicalLibrary { get; set; } = false;

        public bool RequesterSharesEbooks { get; set; } = false;

        // ---- What the addressee shares with the requester ----

        public bool AddresseeSharesPhysicalLibrary { get; set; } = false;

        public bool AddresseeSharesEbooks { get; set; } = false;
    }
}
