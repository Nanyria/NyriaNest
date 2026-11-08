using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using NyriaNest.Api.Tests.TestSupport;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace NyriaNest.Api.Tests.Controllers;

/// <summary>
/// #68: users can upload their own profile picture (POST api/UserActions/avatar),
/// served publicly from GET api/avatars/{userId}/{file}. Otherwise the picture
/// can only be one of the presets, never any URL.
/// </summary>
public class ProfilePictureTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string UploadUrl = "api/UserActions/avatar";
    private const string UpdateUrl = "api/UserActions/update";
    private const string Preset = "assets/images/profile-pictures/RedDragon.png";
    private const int MaxSize = 512 * 1024;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // The first bytes that identify each type, padded with zeros
    private static byte[] Png(int size = 200) => Pad([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], size);
    private static byte[] Jpeg(int size = 200) => Pad([0xFF, 0xD8, 0xFF, 0xE0], size);
    private static byte[] Webp(int size = 200) => Pad([.. "RIFF"u8, 0, 0, 0, 0, .. "WEBP"u8], size);

    // ---- Upload ----

    [Fact]
    public async Task Upload_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync(UploadUrl, Form(Png(), "me.png"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("png")]
    [InlineData("jpg")]
    [InlineData("webp")]
    public async Task Upload_AValidPicture_SavesItUnderMyId_AndMakesItMyPicture(string type)
    {
        var (client, me) = await factory.LoginAsAsync();
        var picture = type switch { "png" => Png(), "jpg" => Jpeg(), _ => Webp() };
        byte[]? saved = null;
        string? savedKey = null;
        factory.FileStorage
            .Setup(s => s.SaveAsync(It.IsAny<Stream>(), It.Is<string>(k => k.StartsWith($"avatars/{me.Id}/")), It.IsAny<CancellationToken>()))
            .Callback((Stream content, string key, CancellationToken _) => { saved = ReadAll(content); savedKey = key; })
            .ReturnsAsync((Stream _, string key, CancellationToken _) => key);

        // The file name doesn't matter: the type comes from the content
        var response = await client.PostAsync(UploadUrl, Form(picture, "anything.bin"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        saved.Should().Equal(picture);
        savedKey.Should().MatchRegex($"^avatars/{me.Id}/[0-9a-f]{{32}}\\.{type}$");
        var url = $"api/{savedKey}";
        (await PictureOf(me.Id)).Should().Be(url);
        (await ResultOf(response)).Should().Be(url);
    }

    [Fact]
    public async Task Upload_Replacing_DeletesMyOldUpload()
    {
        var (client, me) = await factory.LoginAsAsync();
        await client.PostAsync(UploadUrl, Form(Png(), "first.png"), Ct);
        var firstKey = (await PictureOf(me.Id))!["api/".Length..];

        var response = await client.PostAsync(UploadUrl, Form(Png(), "second.png"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.FileStorage.Verify(s => s.DeleteAsync(firstKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    public static TheoryData<string, byte[]> InvalidFiles => new()
    {
        // SVG can carry script, so it's never accepted, whatever it's called
        { "picture.svg", Encoding.UTF8.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>") },
        { "picture.gif", Pad("GIF89a"u8.ToArray(), 200) },
        { "renamed.png", Encoding.UTF8.GetBytes("just some text pretending to be a picture") },
    };

    [Theory]
    [MemberData(nameof(InvalidFiles))]
    public async Task Upload_SomethingThatIsntPngJpegOrWebp_Returns400_AndSavesNothing(string fileName, byte[] content)
    {
        var (client, me) = await factory.LoginAsAsync();

        var response = await client.PostAsync(UploadUrl, Form(content, fileName), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("Only PNG, JPEG and WebP pictures are allowed.");
        AssertNothingSavedFor(me.Id);
        (await PictureOf(me.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Upload_LargerThan512KB_Returns400_AndSavesNothing()
    {
        var (client, me) = await factory.LoginAsAsync();

        var response = await client.PostAsync(UploadUrl, Form(Png(MaxSize + 1), "big.png"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(Ct)).Should().Contain("The picture is larger than 512 KB.");
        AssertNothingSavedFor(me.Id);
    }

    [Fact]
    public async Task Upload_Exactly512KB_Works()
    {
        var (client, _) = await factory.LoginAsAsync();

        var response = await client.PostAsync(UploadUrl, Form(Png(MaxSize), "max.png"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Upload_EmptyFile_Returns400()
    {
        var (client, me) = await factory.LoginAsAsync();

        var response = await client.PostAsync(UploadUrl, Form([], "empty.png"), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        AssertNothingSavedFor(me.Id);
    }

    // ---- Serving ----

    [Fact]
    public async Task Get_ServesTheUpload_WithoutLogin_WithItsType_AndCachedForLong()
    {
        var userId = Guid.NewGuid().ToString();
        var file = $"{Guid.NewGuid():N}.png";
        var picture = Png();
        factory.FileStorage
            .Setup(s => s.OpenReadAsync($"avatars/{userId}/{file}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(picture));
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"api/avatars/{userId}/{file}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await response.Content.ReadAsByteArrayAsync(Ct)).Should().Equal(picture);
        // Every upload gets a new name, so a name never points to another picture
        response.Headers.CacheControl!.Public.Should().BeTrue();
        response.Headers.CacheControl.MaxAge.Should().BeGreaterThanOrEqualTo(TimeSpan.FromDays(30));
        response.Headers.GetValues("X-Content-Type-Options").Should().Contain("nosniff");
    }

    [Fact]
    public async Task Get_AFileThatDoesntExist_Returns404()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"api/avatars/{Guid.NewGuid()}/{Guid.NewGuid():N}.png", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("picture.svg")]
    [InlineData("not-a-generated-name.png")]
    [InlineData("..%2F..%2Fappsettings.json")]
    public async Task Get_ANameWeNeverGenerate_Returns404_WithoutTouchingStorage(string file)
    {
        using var client = factory.CreateClient();
        var userId = Guid.NewGuid().ToString();

        var response = await client.GetAsync($"api/avatars/{userId}/{file}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        factory.FileStorage.Verify(
            s => s.OpenReadAsync(It.Is<string>(k => k.Contains(userId)), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ---- Choosing a picture through update ----

    // #111: the three dragons drawn for NyriaNest replaced the placeholder SVGs
    [Theory]
    [InlineData("assets/images/profile-pictures/RedDragon.png")]
    [InlineData("assets/images/profile-pictures/BlueDragon.png")]
    [InlineData("assets/images/profile-pictures/GreenDragon.png")]
    public async Task Update_ToAPreset_Works(string preset)
    {
        var (client, me) = await factory.LoginAsAsync();

        var response = await client.PutAsJsonAsync(UpdateUrl, new { profilePictureUrl = preset }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await PictureOf(me.Id)).Should().Be(preset);
    }

    // The old placeholders are gone; the migration moved everyone who had one
    [Theory]
    [InlineData("assets/images/profile-pictures/BlueKidDragon.svg")]
    [InlineData("assets/images/profile-pictures/PinkKidDragon.svg")]
    [InlineData("assets/images/profile-pictures/RedDragon.svg")]
    public async Task Update_ToAnOldPlaceholder_Returns400(string oldPreset)
    {
        var (client, me) = await factory.LoginAsAsync();

        var response = await client.PutAsJsonAsync(UpdateUrl, new { profilePictureUrl = oldPreset }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PictureOf(me.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Update_FromMyUploadToAPreset_DeletesTheUpload()
    {
        var (client, me) = await factory.LoginAsAsync();
        await client.PostAsync(UploadUrl, Form(Png(), "me.png"), Ct);
        var uploadKey = (await PictureOf(me.Id))!["api/".Length..];

        var response = await client.PutAsJsonAsync(UpdateUrl, new { profilePictureUrl = Preset }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        factory.FileStorage.Verify(s => s.DeleteAsync(uploadKey, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_WithMyCurrentUpload_KeepsIt()
    {
        var (client, me) = await factory.LoginAsAsync();
        await client.PostAsync(UploadUrl, Form(Png(), "me.png"), Ct);
        var url = await PictureOf(me.Id);

        var response = await client.PutAsJsonAsync(UpdateUrl, new { profilePictureUrl = url, firstName = "Same picture" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await PictureOf(me.Id)).Should().Be(url);
        var key = url!["api/".Length..];
        factory.FileStorage.Verify(s => s.DeleteAsync(key, It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("https://tracker.example/pixel.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("assets/images/profile-pictures/../../index.html")]
    public async Task Update_WithAnyOtherUrl_Returns400_AndKeepsThePicture(string url)
    {
        var (client, me) = await factory.LoginAsAsync();
        await client.PutAsJsonAsync(UpdateUrl, new { profilePictureUrl = Preset }, Ct);

        var response = await client.PutAsJsonAsync(UpdateUrl, new { profilePictureUrl = url }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PictureOf(me.Id)).Should().Be(Preset);
    }

    [Fact]
    public async Task Update_WithSomeoneElsesUpload_Returns400()
    {
        var (otherClient, other) = await factory.LoginAsAsync();
        await otherClient.PostAsync(UploadUrl, Form(Png(), "other.png"), Ct);
        var othersUrl = await PictureOf(other.Id);
        var (client, me) = await factory.LoginAsAsync();

        var response = await client.PutAsJsonAsync(UpdateUrl, new { profilePictureUrl = othersUrl }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PictureOf(me.Id)).Should().BeNull();
    }

    // ---- Helpers ----

    private static byte[] Pad(byte[] start, int size)
    {
        var bytes = new byte[size];
        start.CopyTo(bytes, 0);
        return bytes;
    }

    private static MultipartFormDataContent Form(byte[] content, string fileName)
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private Task<string?> PictureOf(string userId) => factory.WithDbAsync(db =>
        db.Users.Where(u => u.Id == userId).Select(u => u.ProfilePictureUrl).SingleAsync(Ct));

    private static async Task<string?> ResultOf(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return json.RootElement.GetProperty("result").GetString();
    }

    private void AssertNothingSavedFor(string userId) =>
        factory.FileStorage.Verify(
            s => s.SaveAsync(It.IsAny<Stream>(), It.Is<string>(k => k.StartsWith($"avatars/{userId}/")), It.IsAny<CancellationToken>()),
            Times.Never);

    private static byte[] ReadAll(Stream content)
    {
        using var copy = new MemoryStream();
        content.CopyTo(copy);
        return copy.ToArray();
    }
}
