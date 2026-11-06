using NyriaNest.Domain.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace NyriaNest.Domain.Entities
{
    public class StatusHistoryItem
    {
        [Key]
        public int StatusHistoryItemId { get; set; }
        [ForeignKey("Book")]
        public int BookId { get; set; }
        [Required]
        [JsonIgnore]
        public Book Book { get; set; }
        [ForeignKey("User")]
        public string? UserId { get; set; }
        [JsonIgnore]
        public User? User { get; set; }
        [Required]

        public BookStatusEnum BookStatus { get; set; }
        public DateTime? Timestamp { get; set; } = DateTime.UtcNow;

        public string? Notes { get; set; }
    }
}
