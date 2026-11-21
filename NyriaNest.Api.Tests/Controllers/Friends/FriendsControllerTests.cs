using AwesomeAssertions;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Entities;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace NyriaNest.Api.Tests.Controllers.Friends;

/// <summary>
/// #84: the api/friends endpoints. The rules themselves are in FriendshipServiceTests; these check
/// the routes, that "me" comes from the token, the status codes, and that the limits come from
/// configuration (appsettings: 100 / 10).
/// </summary>
public class FriendsControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetFriends_WithoutToken_Returns401()
    {
        var response = await factory.CreateClient().GetAsync("api/friends", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Request_Accept_AndTurnOnSharing_ShowUpInBothOverviews()
    {
        var (me, meUser) = await factory.LoginAsAsync();
        var (anna, annaUser) = await factory.LoginAsAsync();

        var sent = await me.PostAsJsonAsync("api/friends/requests", new { userName = annaUser.UserName }, Ct);
        sent.StatusCode.Should().Be(HttpStatusCode.OK, await sent.Content.ReadAsStringAsync(Ct));
        var id = (await ResultOf(sent)).GetProperty("id").GetInt32();

        var received = (await OverviewOf(anna)).GetProperty("received");
        received.GetArrayLength().Should().Be(1);
        received[0].GetProperty("friendUserName").GetString().Should().Be(meUser.UserName);

        (await anna.PutAsync($"api/friends/requests/{id}/accept", null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var on = await me.PutAsJsonAsync($"api/friends/{id}/sharing/physical-library", new { on = true }, Ct);
        on.StatusCode.Should().Be(HttpStatusCode.OK, await on.Content.ReadAsStringAsync(Ct));

        var mine = await OverviewOf(me);
        var friend = mine.GetProperty("friends")[0];
        friend.GetProperty("friendUserName").GetString().Should().Be(annaUser.UserName);
        friend.GetProperty("iSharePhysicalLibrary").GetBoolean().Should().BeTrue();
        friend.GetProperty("iShareEbooks").GetBoolean().Should().BeFalse();
        mine.GetProperty("physicalLibraryUsed").GetInt32().Should().Be(1);
        mine.GetProperty("physicalLibraryLimit").GetInt32().Should().Be(100);
        mine.GetProperty("ebooksLimit").GetInt32().Should().Be(10);

        var hers = (await OverviewOf(anna)).GetProperty("friends")[0];
        hers.GetProperty("friendSharesPhysicalLibrary").GetBoolean().Should().BeTrue();
        hers.GetProperty("iSharePhysicalLibrary").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Ebook_sharing_has_its_own_route()
    {
        var (me, _) = await factory.LoginAsAsync();
        var (anna, annaUser) = await factory.LoginAsAsync();
        var id = await BecomeFriendsAsync(me, anna, annaUser);

        var on = await me.PutAsJsonAsync($"api/friends/{id}/sharing/ebooks", new { on = true }, Ct);

        on.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await ResultOf(on);
        result.GetProperty("iShareEbooks").GetBoolean().Should().BeTrue();
        result.GetProperty("iSharePhysicalLibrary").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Turning_on_sharing_before_the_request_is_accepted_Returns404()
    {
        var (me, _) = await factory.LoginAsAsync();
        var (_, annaUser) = await factory.LoginAsAsync();
        var sent = await me.PostAsJsonAsync("api/friends/requests", new { userName = annaUser.UserName }, Ct);
        var id = (await ResultOf(sent)).GetProperty("id").GetInt32();

        var on = await me.PutAsJsonAsync($"api/friends/{id}/sharing/ebooks", new { on = true }, Ct);

        on.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Request_to_an_unknown_user_Returns404_and_without_a_user_name_Returns400()
    {
        var (me, _) = await factory.LoginAsAsync();

        (await me.PostAsJsonAsync("api/friends/requests", new { userName = "nobody-here" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await me.PostAsJsonAsync("api/friends/requests", new { }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Someone_else_cant_accept_my_request_Returns404()
    {
        var (me, _) = await factory.LoginAsAsync();
        var (_, annaUser) = await factory.LoginAsAsync();
        var (erik, _) = await factory.LoginAsAsync();
        var sent = await me.PostAsJsonAsync("api/friends/requests", new { userName = annaUser.UserName }, Ct);
        var id = (await ResultOf(sent)).GetProperty("id").GetInt32();

        var response = await erik.PutAsync($"api/friends/requests/{id}/accept", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Decline_and_cancel_answer_200_and_leave_no_open_requests()
    {
        var (me, _) = await factory.LoginAsAsync();
        var (anna, annaUser) = await factory.LoginAsAsync();
        var (_, erikUser) = await factory.LoginAsAsync();
        var toAnna = (await ResultOf(await me.PostAsJsonAsync("api/friends/requests", new { userName = annaUser.UserName }, Ct))).GetProperty("id").GetInt32();
        var toErik = (await ResultOf(await me.PostAsJsonAsync("api/friends/requests", new { userName = erikUser.UserName }, Ct))).GetProperty("id").GetInt32();

        (await anna.PutAsync($"api/friends/requests/{toAnna}/decline", null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await me.DeleteAsync($"api/friends/requests/{toErik}", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        var mine = await OverviewOf(me);
        mine.GetProperty("sent").GetArrayLength().Should().Be(0);
        mine.GetProperty("friends").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Remove_ends_the_friendship_for_both()
    {
        var (me, _) = await factory.LoginAsAsync();
        var (anna, annaUser) = await factory.LoginAsAsync();
        var id = await BecomeFriendsAsync(me, anna, annaUser);

        var response = await anna.DeleteAsync($"api/friends/{id}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await OverviewOf(me)).GetProperty("friends").GetArrayLength().Should().Be(0);
        (await OverviewOf(anna)).GetProperty("friends").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Search_finds_by_the_start_of_the_user_name_and_returns_nothing_personal()
    {
        var (me, _) = await factory.LoginAsAsync();
        var target = await factory.CreateUserAsync();
        var start = target.UserName![..^2];

        var response = await me.GetAsync($"api/friends/search?userName={start}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Ct);
        var found = JsonDocument.Parse(body).RootElement.GetProperty("result");
        found.EnumerateArray().Select(u => u.GetProperty("userName").GetString()).Should().Contain(target.UserName);
        body.Should().NotContain(target.Email!).And.NotContain("firstName");
    }

    // ---- Helpers ----

    private async Task<int> BecomeFriendsAsync(HttpClient from, HttpClient to, User toUser)
    {
        var sent = await from.PostAsJsonAsync("api/friends/requests", new { userName = toUser.UserName }, Ct);
        var id = (await ResultOf(sent)).GetProperty("id").GetInt32();
        (await to.PutAsync($"api/friends/requests/{id}/accept", null, Ct)).EnsureSuccessStatusCode();
        return id;
    }

    private static async Task<JsonElement> OverviewOf(HttpClient client)
    {
        var response = await client.GetAsync("api/friends", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await ResultOf(response);
    }

    private static async Task<JsonElement> ResultOf(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct)).RootElement.GetProperty("result");
}
