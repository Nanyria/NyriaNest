using NyriaNest.Domain.Enums;

namespace NyriaNest.Application.DTOs.Loans
{
    /// <summary>A friend's request to borrow a book (#85), as both the friend and the owner see it.</summary>
    public class BorrowRequestDto
    {
        public int Id { get; set; }
        public int BookId { get; set; }
        public string BookTitle { get; set; } = "";
        public string OwnerUserName { get; set; } = "";
        public string BorrowerId { get; set; } = "";
        public string BorrowerUserName { get; set; } = "";
        public BorrowRequestStatus Status { get; set; }
        /// <summary>Waiting since: when they asked, or reserved, if the queue opened it.</summary>
        public DateTime RequestedAt { get; set; }
        /// <summary>When the book became free for this request.</summary>
        public DateTime AvailableAt { get; set; }
        public DateTime? AnsweredAt { get; set; }
    }

    /// <summary>The body of POST api/borrow-requests. The borrower is whoever is logged in.</summary>
    public class CreateBorrowRequestDto
    {
        public int BookId { get; set; }
    }
}
