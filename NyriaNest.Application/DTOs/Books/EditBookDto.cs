namespace NyriaNest.Application.DTOs.Books
{
    /// <summary>
    /// What the edit book page loads (GET api/Book/{id}): the editable fields from
    /// SlimBookDto, plus what only the server sets. The page still sends back a plain
    /// SlimBookDto, so a client can't set the EPUB fields (#76).
    /// </summary>
    public class EditBookDto : SlimBookDto
    {
        public int BookId { get; set; }
        public bool HasEpub { get; set; }
        public string? EpubFileName { get; set; }
        public long? EpubFileSize { get; set; }
    }
}
