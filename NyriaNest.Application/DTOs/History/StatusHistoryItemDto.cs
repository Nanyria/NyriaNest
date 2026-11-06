using NyriaNest.Application.DTOs.Books;
using NyriaNest.Domain.Enums;


namespace NyriaNest.Application.DTOs.History
{
    public class StatusHistoryItemDto
    {
        public int StatusHistoryItemId { get; set; }
        public BookStatusEnum BookStatus { get; set; }
        public DateTime? Timestamp { get; set; } = DateTime.UtcNow;
        public string? UserID { get; set; }
        public int? BookID { get; set; }
        public SlimBookDto? Book { get; set; }
        public string? Notes { get; set; }
    }
}
