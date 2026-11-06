
using System.ComponentModel.DataAnnotations;

namespace NyriaNest.Api.Models
{
    public class CreateRatingItemDto
    {
        [Range(0, 10, ErrorMessage = "Rating must be between 0 and 10.")]
        public int Rating { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}

