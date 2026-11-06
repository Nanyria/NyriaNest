using NyriaNest.Api.Helpers.Enums;
using NyriaNest.Api.Models.History;
using NyriaNest.Api.Models.History.HistoryDTOs;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NyriaNest.Api.Models.Books.BookDTOs
{

    public class SlimBookDto
    {
        // Book Information
        [Required]
        [MaxLength(100)]
        public required string Title { get; set; }

        [Required]
        [MaxLength(75)]
        public required string Author { get; set; }
        public string? BookDescription { get; set; }
        public int PublicationYear { get; set; }
        public string? CoverImagePath { get; set; }

        // Classification
        public GenreEnums Genre { get; set; }
        public BookTypeEnums BookType { get; set; }
        public LanguageEnums Language { get; set; }

    }
}

