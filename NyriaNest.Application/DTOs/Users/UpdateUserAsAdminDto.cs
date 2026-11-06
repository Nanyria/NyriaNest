using NyriaNest.Domain.Entities;

namespace NyriaNest.Application.DTOs.Users
{
    public class UpdateUserAsAdminDto
    {
        public string? UserName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? Role { get; set; }

        public List<Book>? BorrowedBooks { get; set; } = new List<Book>();
        public List<Book>? ReservedBooks { get; set; } = new List<Book>();
        public List<UserHistory>? UserHistory { get; set; } = new List<UserHistory>();
    }
}
