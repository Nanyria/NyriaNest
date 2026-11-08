using Microsoft.AspNetCore.Identity;
using NyriaNest.Application.Common;
using NyriaNest.Application.Interfaces;
using NyriaNest.Domain.Constants;
using NyriaNest.Domain.Entities;
using System.Net;
using System.Text.RegularExpressions;

namespace NyriaNest.Application.Services
{
    public record ProfilePictureFile(Stream Content, string ContentType);

    public interface IProfilePictureService
    {
        Task<APIResponse<string>> UploadAsync(string userId, UploadedFile? file, CancellationToken ct = default);
        Task<ProfilePictureFile?> OpenAsync(string userId, string fileName, CancellationToken ct = default);
        bool IsAllowed(string url, User user);
        Task DeleteUploadAsync(string? url, CancellationToken ct = default);
    }

    /// <summary>
    /// Profile pictures users upload themselves. They're stored under
    /// "avatars/{userId}/{random}.{ext}" and served publicly from
    /// "api/avatars/{userId}/{random}.{ext}", the URL saved on the user.
    /// The browser crops and shrinks a picture to 512 x 512 before uploading,
    /// so the limit here only stops uploads that skip the page.
    /// </summary>
    public partial class ProfilePictureService : IProfilePictureService
    {
        public const long MaxFileSize = 512 * 1024; // 512 KB
        private const string UrlPrefix = "api/";
        private const string KeyPrefix = "avatars/";

        private readonly IFileStorage _storage;
        private readonly UserManager<User> _userManager;

        public ProfilePictureService(IFileStorage storage, UserManager<User> userManager)
        {
            _storage = storage;
            _userManager = userManager;
        }

        public async Task<APIResponse<string>> UploadAsync(string userId, UploadedFile? file, CancellationToken ct = default)
        {
            var response = new APIResponse<string> { IsSuccess = false, StatusCode = HttpStatusCode.BadRequest };

            if (file is null || file.Length == 0)
                return Fail(response, "No picture was uploaded.");
            if (file.Length > MaxFileSize)
                return Fail(response, "The picture is larger than 512 KB.");

            // The type comes from the first bytes, never from the name: a renamed
            // SVG (which can carry script) or any other file is turned away
            var type = await DetectTypeAsync(file, ct);
            if (type is null)
                return Fail(response, "Only PNG, JPEG and WebP pictures are allowed.");

            var user = await _userManager.FindByIdAsync(userId);
            if (user is null)
            {
                response.StatusCode = HttpStatusCode.NotFound;
                return Fail(response, "User not found.");
            }

            var key = $"{KeyPrefix}{userId}/{Guid.NewGuid():N}.{type.Value.Extension}";
            await using (var stream = file.OpenReadStream())
            {
                await _storage.SaveAsync(stream, key, ct);
            }

            var oldUrl = user.ProfilePictureUrl;
            user.ProfilePictureUrl = UrlPrefix + key;
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                // Don't leave an orphaned file behind if the user couldn't be saved
                await _storage.DeleteAsync(key, CancellationToken.None);
                response.ErrorMessages.AddRange(result.Errors.Select(e => e.Description));
                return response;
            }

            await DeleteUploadAsync(oldUrl, ct);

            response.IsSuccess = true;
            response.StatusCode = HttpStatusCode.OK;
            response.Result = user.ProfilePictureUrl;
            return response;
        }

        public async Task<ProfilePictureFile?> OpenAsync(string userId, string fileName, CancellationToken ct = default)
        {
            // Only names this service generates, so nothing else in storage can be reached
            if (!Guid.TryParse(userId, out _) || !GeneratedName().IsMatch(fileName))
                return null;

            var stream = await _storage.OpenReadAsync($"{KeyPrefix}{userId}/{fileName}", ct);
            if (stream is null)
                return null;

            var extension = Path.GetExtension(fileName).TrimStart('.');
            var contentType = Types.First(t => t.Extension == extension).ContentType;
            return new ProfilePictureFile(stream, contentType);
        }

        /// <summary>A preset, or the user's own current upload. Never any other URL.</summary>
        public bool IsAllowed(string url, User user) =>
            ProfilePictures.Presets.Contains(url)
            || (url == user.ProfilePictureUrl && UploadKey(url) is not null);

        /// <summary>Deletes the file behind an uploaded picture's URL; presets and null are ignored.</summary>
        public async Task DeleteUploadAsync(string? url, CancellationToken ct = default)
        {
            var key = UploadKey(url);
            if (key is not null)
                await _storage.DeleteAsync(key, ct);
        }

        private static string? UploadKey(string? url) =>
            url is not null && url.StartsWith(UrlPrefix + KeyPrefix, StringComparison.Ordinal)
                ? url[UrlPrefix.Length..]
                : null;

        private static readonly (string Extension, string ContentType, Func<byte[], bool> Matches)[] Types =
        [
            ("png", "image/png", h => h.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })),
            ("jpg", "image/jpeg", h => h.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })),
            ("webp", "image/webp", h => h.Length >= 12 && h.AsSpan(0, 4).SequenceEqual("RIFF"u8) && h.AsSpan(8, 4).SequenceEqual("WEBP"u8)),
        ];

        private static async Task<(string Extension, string ContentType)?> DetectTypeAsync(UploadedFile file, CancellationToken ct)
        {
            var header = new byte[12];
            await using var stream = file.OpenReadStream();
            var read = await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
            var start = header[..read];

            foreach (var type in Types)
            {
                if (type.Matches(start))
                    return (type.Extension, type.ContentType);
            }
            return null;
        }

        private static APIResponse<string> Fail(APIResponse<string> response, string message)
        {
            response.ErrorMessages.Add(message);
            return response;
        }

        [GeneratedRegex("^[0-9a-f]{32}\\.(png|jpg|webp)$")]
        private static partial Regex GeneratedName();
    }
}
