using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace NyriaNest.Domain.Entities
{
    public class ReservationItem
    {
        [Key]
        public int Id { get; set; }
        [Required]
        [ForeignKey("User")]
        public string UserId { get; set; }
        [JsonIgnore]
        public User User { get; set; }
        [Required]
        [ForeignKey("Book")]
        public int BookId { get; set; }

        public Book Book { get; set; }
        public DateTime ReservationDate { get; set; }

        public DateTime? AvailabilityDate { get; set; }

        public DateTime? BookIsAvaliableEmailSent { get; set; }
    }
}
