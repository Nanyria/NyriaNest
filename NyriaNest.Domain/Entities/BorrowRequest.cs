using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using NyriaNest.Domain.Enums;

namespace NyriaNest.Domain.Entities
{
    /// <summary>
    /// A friend asking to borrow someone's physical book (#85). Accepting it lends the book as a
    /// normal loan; the request stays as Lent. The owner is the book's owner, so it isn't stored
    /// here. Whether the friend may ask is checked when they ask (the owner shares their physical
    /// library with them), not kept on the row. Goes with the book when the book is deleted.
    /// </summary>
    public class BorrowRequest
    {
        [Key]
        public int Id { get; set; }

        public int BookId { get; set; }

        [ForeignKey("BookId")]
        [JsonIgnore]
        public Book Book { get; set; } = null!;

        /// <summary>The friend who wants to borrow the book.</summary>
        [Required]
        public string BorrowerId { get; set; } = null!;

        [ForeignKey("BorrowerId")]
        [JsonIgnore]
        public User Borrower { get; set; } = null!;

        public BorrowRequestStatus Status { get; set; } = BorrowRequestStatus.Requested;

        /// <summary>
        /// When the friend asked, or when the queue handed it to them. A request is only made
        /// while the book is free, so the reminder and the expiry count from here.
        /// </summary>
        public DateTime RequestedAt { get; set; }

        /// <summary>When it stopped being Requested, whatever the reason (lent, declined, cancelled, expired).</summary>
        public DateTime? AnsweredAt { get; set; }

        /// <summary>When the owner was last reminded to answer, so the daily job reminds once per period.</summary>
        public DateTime? LastReminderAt { get; set; }
    }
}
