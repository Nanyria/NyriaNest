namespace NyriaNest.Application.Interfaces
{
    /// <summary>
    /// Stores files by key (e.g. "42/3f9a....epub"). Implementations can save to
    /// local disk, Azure Blob Storage, S3 etc. without the rest of the app changing.
    /// </summary>
    public interface IFileStorage
    {
        Task<string> SaveAsync(Stream content, string key, CancellationToken ct = default);
        Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default);
        Task DeleteAsync(string key, CancellationToken ct = default);
    }
}
