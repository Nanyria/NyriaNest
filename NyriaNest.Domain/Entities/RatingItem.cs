using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace NyriaNest.Domain.Entities
{
    public class RatingItem
    {
        public int Id { get; set; }

        [ForeignKey("ReviewItem")]
        public int ReviewItemId { get; set; }

        [JsonIgnore]
        public ReviewItem ReviewItem { get; set; }

        [Range(0, 10, ErrorMessage = "Rating must be between 0 and 10.")]
        public int Rating { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

