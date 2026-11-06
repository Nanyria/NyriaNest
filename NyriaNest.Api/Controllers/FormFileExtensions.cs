using NyriaNest.Api.Storage;

namespace NyriaNest.Api.Controllers
{
    public static class FormFileExtensions
    {
        /// <summary>Hands an uploaded file to the services without ASP.NET's IFormFile.</summary>
        public static UploadedFile ToUploadedFile(this IFormFile file) =>
            new(file.FileName, file.Length, file.OpenReadStream);
    }
}
