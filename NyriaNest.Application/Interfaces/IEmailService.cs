
namespace NyriaNest.Application.Interfaces
{
    public interface IEmailService
    {
        Task SendEmailAsync(string to, string subject, string body);
        // No password parameters: a password is never sent by email or written to a log
        Task SendRegistrationEmailAsync(string to, string firstName, string userName);
        Task SendBookBorrowedEmailAsync(string to, string firstname, string userName, string bookTitle, DateTime returnDate);
        Task SendBookReturnedEmailAsync(string to, string firstname, string userName, string bookTitle);
        Task SendBookOverdueEmailAsync(string to, string firstname, string userName, string bookTitle, DateTime dueDate);

    }
}
