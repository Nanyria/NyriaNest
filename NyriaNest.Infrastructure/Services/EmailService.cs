using Microsoft.Extensions.Configuration;
using NyriaNest.Application.Interfaces;
using System.Net;
using System.Net.Mail;

namespace NyriaNest.Infrastructure.Services
{
    public class EmailService : IEmailService
    {
        private readonly SmtpClient _smtpClient;
        private readonly string _fromAddress;

        public EmailService(IConfiguration configuration)
        {
            _fromAddress = configuration["Email:From"];
            _smtpClient = new SmtpClient(configuration["Email:SmtpHost"])
            {
                Port = int.Parse(configuration["Email:SmtpPort"]),
                Credentials = new NetworkCredential(
                    configuration["Email:Username"],
                    configuration["Email:Password"]),
                EnableSsl = true
            };
        }

        public async Task SendEmailAsync(string to, string subject, string body)
        {
            if (string.IsNullOrWhiteSpace(_fromAddress))
                throw new InvalidOperationException("From address is not configured.");

            if (string.IsNullOrWhiteSpace(to))
                throw new ArgumentException("Recipient address cannot be empty.", nameof(to));

            try
            {
                // This will throw if the addresses are not valid
                var mailMessage = new MailMessage(_fromAddress, to, subject, body);
                await _smtpClient.SendMailAsync(mailMessage);
            }
            catch (FormatException ex)
            {
                // Log or handle invalid email address format
                throw new ArgumentException("Invalid email address format.", ex);
            }
            catch (SmtpException ex)
            {
                // Log or handle SMTP errors
                throw new InvalidOperationException("Failed to send email.", ex);
            }
        }

        public async Task SendRegistrationEmailAsync(string to, string firstName, string userName)
        {
            var subject = "Welcome to NyriaNest!";
            var body = $"Hi {firstName},\n\nWelcome to NyriaNest, we're glad you joined!\n\n" +
                $"Your username is {userName}. Log in with the password you chose when you signed up.";
            await SendEmailAsync(to, subject, body);
        }
        public async Task SendBookBorrowedEmailAsync(string to, string firstname, string userName, string bookTitle, DateTime returnDate)
        {
            var subject = "Bekräftelse av lån";
            var body = $"Hej {firstname},\n\nHär en en bekräftelse av ditt lån av \"{bookTitle}\"." +
                $"\n Återlämningsdatum:{returnDate:yyyy-MM-dd}\n Hoppas du gillar boken, lämna gärna en recession!";
            await SendEmailAsync(to, subject, body);
        }

        public async Task SendBookReturnedEmailAsync(string to, string firstname, string userName, string bookTitle)
        {
            var subject = "Bekräftelse av återlämning av bok";
            var body = $"Hej {userName},\n\nDu har precis återlämnat \"{bookTitle}\". Tack!\n Lämna gärna en recenssion";
            await SendEmailAsync(to, subject, body);
        }

        public async Task SendBookOverdueEmailAsync(string to, string firstname, string userName, string bookTitle, DateTime dueDate)
        {
            var subject = "Återlämningsdatum passerat";
            var body = $"Hej {firstname},\n\nBoken \"{bookTitle}\" skulle vara återlämnad {dueDate:yyyy-MM-dd}. Vänligen lämna tillbaka boken så fort som möjligt.";
            await SendEmailAsync(to, subject, body);
        }
    }
}

