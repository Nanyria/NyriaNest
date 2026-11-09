namespace NyriaNest.Application.DTOs.Users
{
    public class UpdateUserDto
    {
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? Password { get; set; }
        // Required when changing Password or Email, so a stolen session alone can't take over the account
        public string? CurrentPassword { get; set; }
        public string? ProfilePictureUrl { get; set; } // Add this for profile picture selection
        public string? Bio { get; set; } // Add this for biograph

        // Only in the response: a new login token when the change ended the user's
        // sessions (a new password or email), so the device that made it stays logged in
        public string? Token { get; set; }
    }
}
