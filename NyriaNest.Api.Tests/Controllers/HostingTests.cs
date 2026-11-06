using AwesomeAssertions;
using NyriaNest.Api.Tests.TestSupport;

namespace NyriaNest.Api.Tests.Controllers;

/// <summary>
/// Security finding 7 (#48): only the frontend's origin passes CORS, and
/// Swagger is only served in Development (these tests run as "Testing").
/// </summary>
public class HostingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Cors_AllowsTheConfiguredFrontendOrigin()
    {
        var response = await Preflight(ApiFactory.AllowedOrigin);

        response.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowed).Should().BeTrue();
        allowed.Should().Equal(ApiFactory.AllowedOrigin);
    }

    [Fact]
    public async Task Cors_RejectsAnyOtherOrigin()
    {
        var response = await Preflight("https://evil.example");

        response.Headers.Contains("Access-Control-Allow-Origin").Should().BeFalse();
    }

    [Theory]
    [InlineData("swagger/v1/swagger.json")]
    [InlineData("swagger/index.html")]
    public async Task Swagger_IsNotServed_OutsideDevelopment(string path)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        body.Should().NotContain("\"openapi\"").And.NotContainEquivalentOf("swagger-ui");
    }

    // What a browser sends before a cross-origin request with a token
    private async Task<HttpResponseMessage> Preflight(string origin)
    {
        using var client = factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Options, "api/Book");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");
        return await client.SendAsync(request, Ct);
    }
}
