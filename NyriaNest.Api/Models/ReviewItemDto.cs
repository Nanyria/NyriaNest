using NyriaNest.Api.Models.Books.BookDTOs;

namespace NyriaNest.Api.Models
{
    public class ReviewItemDto
    {
        public int Id { get; set; }
        public string UserId { get; set; }
        public int BookId { get; set; }
        public SlimBookDto Book { get; set; }
        public string? ReviewHeader { get; set; }
        public string? ReviewText { get; set; }
        public RatingItemDto? RatingItem { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
    public class CreateReviewItemDto
    {
        public string UserId { get; set; }
        public int BookId { get; set; }
        public string? ReviewHeader { get; set; }
        public string? ReviewText { get; set; }
        public CreateRatingItemDto? RatingItem { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
    public class BookReviewDto
    {
        public string UserName { get; set; } // Add this
        public string? ReviewHeader { get; set; }
        public string? ReviewText { get; set; }
        public RatingItemDto? RatingItem { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
