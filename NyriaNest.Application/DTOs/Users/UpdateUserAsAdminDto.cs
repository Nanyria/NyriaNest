namespace NyriaNest.Application.DTOs.Users
{
    /// <summary>
    /// What an admin may change on another user. Null means "leave as it is".
    /// No lists: a user's loans, reservations and history are changed through
    /// their own endpoints (#50), never by editing the user (#56).
    /// </summary>
    public class UpdateUserAsAdminDto
    {
        public string? UserName { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public string? Password { get; set; }
        public string? Role { get; set; }
    }
}
