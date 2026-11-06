using System.ComponentModel.DataAnnotations;

namespace NyriaNest.Application.DTOs.Users
{
    public class CreateAdminUserDto
    {
        [Required]
        [MaxLength(100)]
        public required string UserName { get; set; }

        [Required]
        [MaxLength(100)]
        public required string FirstName { get; set; }

        [Required]
        [MaxLength(100)]
        public required string LastName { get; set; }

        [Required]
        [MaxLength(100)]
        public required string Email { get; set; }

        [Required]
        [MaxLength(100)]
        public required string Password { get; set; }

        [Required]
        [MaxLength(50)]
        public required string Role { get; set; }
    }
}
