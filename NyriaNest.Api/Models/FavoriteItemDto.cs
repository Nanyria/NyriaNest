using NyriaNest.Api.Models.Books.BookDTOs;


namespace NyriaNest.Api.Models
{
    public class FavoriteItemDto
    {
        public string UserId { get; set; }
        public int BookId { get; set; }
        public SlimBookDto Book { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
