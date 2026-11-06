using NyriaNest.Application.DTOs.Books;


namespace NyriaNest.Application.DTOs
{
    public class FavoriteItemDto
    {
        public string UserId { get; set; }
        public int BookId { get; set; }
        public SlimBookDto Book { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
