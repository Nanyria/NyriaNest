using NyriaNest.Domain.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
namespace NyriaNest.Domain.Entities
{
    public class Book
    {
        //Identiy
        [Key]
        public int BookId { get; set; }
        [Required]

        // Book Information
        [MaxLength(100)]
        public required string Title { get; set; }

        [Required]
        [MaxLength(75)]
        public required string Author { get; set; }
        public string? BookDescription { get; set; }
        public int PublicationYear { get; set; }
        public string? CoverImagePath { get; set; }

        // Classification
        public GenreEnums Genre { get; set; }
        public BookTypeEnums BookType { get; set; }
        public LanguageEnums Language { get; set; }

        // Library Status
        public required BookStatusEnum BookStatus { get; set; }
        public DateTime AvailabilityDate { get; set; }
        public DateTime DateAdded { get; set; } = DateTime.UtcNow;

        // Storage key for the EPUB file, e.g. "42/3f9a....epub". Never sent to clients.
        [JsonIgnore]
        [MaxLength(200)]
        public string? EpubFileKey { get; set; }

        // What clients see instead of the key
        [NotMapped]
        public bool HasEpub => EpubFileKey != null;

        // The uploaded file's original name and size in bytes, shown on the edit page.
        // Null for files uploaded before these were stored.
        [MaxLength(255)]
        public string? EpubFileName { get; set; }
        public long? EpubFileSize { get; set; }

        // Navigation properties
        public List<ReviewItem> Reviews { get; set; } = new();
        public List<StatusHistoryItem> StatusHistory { get; set; } = new();
        public List<ReservationItem> Reservations { get; set; } = new();
        public CheckedOutItem? CheckedOutBy { get; set; }

        // Set only by an admin (Lost or Removed). When set, it wins over loans and reservations.
        public BookStatusEnum? AdminStatus { get; set; }
        public DateTime? AdminStatusSetAt { get; set; }
        [MaxLength(500)]
        public string? AdminStatusNotes { get; set; }

    }
}
