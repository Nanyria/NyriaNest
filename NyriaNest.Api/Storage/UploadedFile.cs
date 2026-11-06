namespace NyriaNest.Api.Storage
{
    /// <summary>
    /// A file sent by a client, without ASP.NET's IFormFile, so the services
    /// don't depend on the web layer. <see cref="OpenReadStream"/> opens a new
    /// stream from the start each time: the EPUB check reads the header, and
    /// the save then reads the whole file.
    /// </summary>
    public sealed record UploadedFile(string FileName, long Length, Func<Stream> OpenReadStream);
}
