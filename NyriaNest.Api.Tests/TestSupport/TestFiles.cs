using System.Net.Http.Headers;

namespace NyriaNest.Api.Tests.TestSupport;

/// <summary>Helpers for the file upload tests (profile pictures, EPUBs).</summary>
public static class TestFiles
{
    public const string EpubContentType = "application/epub+zip";

    /// <summary>A multipart form with the file in the "file" field, as the upload endpoints expect.</summary>
    public static MultipartFormDataContent Form(byte[] content, string fileName, string contentType = "application/octet-stream")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    /// <summary>Copies a stream into a byte array (to capture what was saved to storage).</summary>
    public static byte[] ReadAll(Stream content)
    {
        using var copy = new MemoryStream();
        content.CopyTo(copy);
        return copy.ToArray();
    }
}
