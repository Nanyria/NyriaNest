using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace NyriaNest.Api.Tests.Controllers;

/// <summary>
/// Pins down how EPUB uploads behave today (POST api/Book/{id}/epub and
/// POST api/Book/ebook), so #47 can move the upload code without changing it.
/// </summary>
public class EpubUploadTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ---- POST api/Book/{bookId}/epub ----

    [Fact]
    public async Task Upload_ValidEpub_SavesTheWholeFile_AndItsName()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var bookId = await AddBookAsync(BookTypeEnums.EBook);
        var epub = ValidEpub();
        byte[]? saved = null;
        factory.FileStorage
            .Setup(s => s.SaveAsync(It.IsAny<Stream>(), It.Is<string>(k => k.StartsWith($"{bookId}/")), It.IsAny<CancellationToken>()))
            .Callback((Stream content, string _, CancellationToken _) => saved = ReadAll(content))
            .ReturnsAsync((Stream _, string key, CancellationToken _) => key);

        var response = await admin.PostAsync($"api/Book/{bookId}/epub", Form(epub, "My Book.epub"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        saved.Should().Equal(epub, "the file is read again after validation, from the start");
        var book = await factory.WithDbAsync(db => db.Books.SingleAsync(b => b.BookId == bookId, Ct));
        book.EpubFileKey.Should().StartWith($"{bookId}/").And.EndWith(".epub");
        book.EpubFileName.Should().Be("My Book.epub");
        book.EpubFileSize.Should().Be(epub.Length);
    }

    [Fact]
    public async Task Upload_Replacing_DeletesTheOldFile()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var bookId = await AddBookAsync(BookTypeEnums.EBook);
        await admin.PostAsync($"api/Book/{bookId}/epub", Form(ValidEpub(), "first.epub"), Ct);
        var firstKey = await factory.WithDbAsync(db =>
            db.Books.Where(b => b.BookId == bookId).Select(b => b.EpubFileKey).SingleAsync(Ct));

        var response = await admin.PostAsync($"api/Book/{bookId}/epub", Form(ValidEpub(), "second.epub"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.FileStorage.Verify(s => s.DeleteAsync(firstKey!, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("book.pdf", true, "Only .epub files are allowed.")]
    [InlineData("renamed.epub", false, "The file is not a valid EPUB.")]
    public async Task Upload_InvalidFile_Returns400_AndSavesNothing(string fileName, bool validContent, string message)
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var bookId = await AddBookAsync(BookTypeEnums.EBook);
        var content = validContent ? ValidEpub() : Encoding.UTF8.GetBytes("just some text, not a zip file at all, long enough to read");

        var response = await admin.PostAsync($"api/Book/{bookId}/epub", Form(content, fileName), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain(message);
        AssertNothingSavedFor(bookId);
    }

    [Fact]
    public async Task Upload_EmptyFile_Returns400()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var bookId = await AddBookAsync(BookTypeEnums.EBook);

        var response = await admin.PostAsync($"api/Book/{bookId}/epub", Form([], "empty.epub"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        AssertNothingSavedFor(bookId);
    }

    [Fact]
    public async Task Upload_ToAPaperback_Returns400()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var bookId = await AddBookAsync(BookTypeEnums.Paperback);

        var response = await admin.PostAsync($"api/Book/{bookId}/epub", Form(ValidEpub(), "book.epub"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("Only e-books can have an EPUB file.");
        AssertNothingSavedFor(bookId);
    }

    // ---- POST api/Book/ebook (book and file in one request) ----

    [Fact]
    public async Task AddEbook_WithValidEpub_CreatesTheEbookWithItsFile()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var title = $"E-book {Guid.NewGuid():N}";

        var response = await admin.PostAsync("api/Book/ebook", EbookForm(title, ValidEpub(), "new.epub"), Ct);

        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(Ct));
        var book = await factory.WithDbAsync(db => db.Books.SingleAsync(b => b.Title == title, Ct));
        book.BookType.Should().Be(BookTypeEnums.EBook);
        book.EpubFileName.Should().Be("new.epub");
        book.EpubFileKey.Should().StartWith($"{book.BookId}/");
    }

    [Fact]
    public async Task AddEbook_WithInvalidFile_CreatesNoBook()
    {
        var (admin, _) = await factory.LoginAsAsync(Roles.Admin);
        var title = $"E-book {Guid.NewGuid():N}";

        var response = await admin.PostAsync("api/Book/ebook", EbookForm(title, ValidEpub(), "new.pdf"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await factory.WithDbAsync(db => db.Books.AnyAsync(b => b.Title == title, Ct))).Should().BeFalse();
    }

    // ---- Helpers ----

    /// <summary>
    /// The start of a real EPUB: a ZIP local file header for an uncompressed
    /// "mimetype" entry containing "application/epub+zip", then some bytes.
    /// That's what the API checks.
    /// </summary>
    private static byte[] ValidEpub()
    {
        var name = Encoding.ASCII.GetBytes("mimetype");
        var mime = Encoding.ASCII.GetBytes("application/epub+zip");
        var header = new byte[30];
        header[0] = 0x50; header[1] = 0x4B; header[2] = 0x03; header[3] = 0x04; // "PK\3\4"
        BitConverter.GetBytes((uint)mime.Length).CopyTo(header, 18); // compressed size
        BitConverter.GetBytes((uint)mime.Length).CopyTo(header, 22); // uncompressed size
        BitConverter.GetBytes((ushort)name.Length).CopyTo(header, 26);
        return [.. header, .. name, .. mime, .. Encoding.ASCII.GetBytes("…rest of the book…")];
    }

    private static MultipartFormDataContent Form(byte[] content, string fileName)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/epub+zip");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static MultipartFormDataContent EbookForm(string title, byte[] content, string fileName)
    {
        var form = Form(content, fileName);
        form.Add(new StringContent(title), "title");
        form.Add(new StringContent("Test Author"), "author");
        form.Add(new StringContent("2020"), "publicationYear");
        form.Add(new StringContent(((int)GenreEnums.Fiction).ToString()), "genre");
        form.Add(new StringContent(((int)LanguageEnums.English).ToString()), "language");
        return form;
    }

    private void AssertNothingSavedFor(int bookId) =>
        factory.FileStorage.Verify(
            s => s.SaveAsync(It.IsAny<Stream>(), It.Is<string>(k => k.StartsWith($"{bookId}/")), It.IsAny<CancellationToken>()),
            Times.Never);

    private static byte[] ReadAll(Stream content)
    {
        using var copy = new MemoryStream();
        content.CopyTo(copy);
        return copy.ToArray();
    }

    private Task<int> AddBookAsync(BookTypeEnums type) => factory.WithDbAsync(async db =>
    {
        var book = TestData.Book(type: type);
        db.Books.Add(book);
        await db.SaveChangesAsync(Ct);
        return book.BookId;
    });
}
