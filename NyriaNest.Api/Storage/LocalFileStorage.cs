using NyriaNest.Application.Interfaces;

namespace NyriaNest.Api.Storage
{
    /// <summary>
    /// Saves files on local disk, outside wwwroot, so they can only be
    /// downloaded through the API (which checks permissions).
    /// </summary>
    public class LocalFileStorage : IFileStorage
    {
        private readonly string _root;

        public LocalFileStorage(IConfiguration config, IWebHostEnvironment env)
        {
            var relativePath = config["Storage:LocalPath"] ?? "App_Data/epubs";
            _root = Path.GetFullPath(Path.Combine(env.ContentRootPath, relativePath));
            Directory.CreateDirectory(_root);
        }

        public async Task<string> SaveAsync(Stream content, string key, CancellationToken ct = default)
        {
            var path = FullPath(key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            await using var file = File.Create(path);
            await content.CopyToAsync(file, ct);
            return key;
        }

        public Task<Stream?> OpenReadAsync(string key, CancellationToken ct = default)
        {
            var path = FullPath(key);
            Stream? stream = File.Exists(path) ? File.OpenRead(path) : null;
            return Task.FromResult(stream);
        }

        public Task DeleteAsync(string key, CancellationToken ct = default)
        {
            var path = FullPath(key);
            if (File.Exists(path))
                File.Delete(path);
            return Task.CompletedTask;
        }

        private string FullPath(string key)
        {
            var path = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));

            // Never allow a key like "../../appsettings.json" to escape the storage folder
            if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Invalid file key.", nameof(key));

            return path;
        }
    }
}

