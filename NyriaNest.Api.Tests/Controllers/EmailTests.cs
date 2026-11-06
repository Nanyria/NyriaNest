using AwesomeAssertions;
using NyriaNest.Api.Tests.TestSupport;
using System.Net;
using System.Net.Http.Json;

namespace NyriaNest.Api.Tests.Controllers;

/// <summary>
/// Security findings 5 and 6 (#48): passwords are never emailed, and there's
/// no open endpoint that sends email.
/// </summary>
public class EmailTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Register_SendsAWelcomeEmail_WithoutThePassword()
    {
        using var client = factory.CreateClient();
        var name = $"new-{Guid.NewGuid():N}"[..12];
        var password = $"Unique-{Guid.NewGuid():N}-1!";

        var response = await client.PostAsJsonAsync("api/UserActions", new
        {
            userName = name,
            firstName = "New",
            lastName = "Reader",
            email = $"{name}@test.local",
            password,
        }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var emailsToNewUser = factory.Email.Invocations
            .Where(i => i.Arguments.Contains($"{name}@test.local"))
            .ToList();
        emailsToNewUser.Should().ContainSingle("the new user gets a welcome email");
        emailsToNewUser.SelectMany(i => i.Arguments).Should().NotContain(password);
    }

    [Fact]
    public async Task TestEmailEndpoint_IsGone()
    {
        // It sent email to a hard-coded address without needing a login
        using var client = factory.CreateClient();

        var response = await client.PostAsync("api/Email/send", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
