using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace NyriaNest.Domain.Entities
{
    public class CheckedOutItem
    {
        [Key]
        public int Id { get; set; }
        // #83: a loan goes to a member (UserId) or to someone without an account
        // (BorrowerName), exactly one of them (a check constraint in AppDbContext)
        [ForeignKey("User")]
        public string? UserId { get; set; }
        [JsonIgnore]
        public User? User { get; set; }
        [MaxLength(100)]
        public string? BorrowerName { get; set; }
        [ForeignKey("Book")]
        public int BookId { get; set; }
        [JsonIgnore]
        public Book Book { get; set; }
        public DateTime CheckOutDate { get; set; }
        public DateTime ReturnDate { get; set; }
        public DateTime? ReminderEmailSent { get; set; }
    }
}
