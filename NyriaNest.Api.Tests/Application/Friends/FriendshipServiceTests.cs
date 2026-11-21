using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Application.Friends;
using NyriaNest.Application.Notifications;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using NyriaNest.Infrastructure.Data;
using NyriaNest.Infrastructure.Repositories;

namespace NyriaNest.Api.Tests.Application.Friends;

/// <summary>
/// Friend requests and the sharing settings (#84, ADR 0005). The limits are set low here
/// (2 physical / 1 e-books) so the "one too many" tests stay small; the defaults are checked separately.
/// </summary>
public class FriendshipServiceTests : IDisposable
{
    private const int PhysicalLimit = 2;
    private const int EbookLimit = 1;

    private readonly SqliteDb _db = new();
    private readonly User _me;
    private readonly User _anna;
    private readonly User _erik;
    private readonly User _olle;

    public FriendshipServiceTests()
    {
        using var db = _db.CreateContext();
        _me = TestData.User("me");
        _anna = TestData.User("anna");
        _erik = TestData.User("erik");
        _olle = TestData.User("olle");
        db.Users.AddRange(_me, _anna, _erik, _olle);
        db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private FriendshipService Service(AppDbContext db) =>
        new(db, new UserRepo(db), new NotificationService(db),
            Options.Create(new SharingLimitsOptions { PhysicalLibrary = PhysicalLimit, Ebooks = EbookLimit }));

    /// <summary>Sends a request from <paramref name="from"/> and lets <paramref name="to"/> accept it.</summary>
    private async Task<int> BecomeFriendsAsync(User from, User to)
    {
        using var db = _db.CreateContext();
        var service = Service(db);
        var sent = await service.SendRequestAsync(from.Id, to.UserName!);
        sent.IsSuccess.Should().BeTrue(string.Join(", ", sent.ErrorMessages));
        var accepted = await service.AcceptAsync(to.Id, sent.Result.Id);
        accepted.IsSuccess.Should().BeTrue(string.Join(", ", accepted.ErrorMessages));
        return sent.Result.Id;
    }

    [Fact]
    public void The_default_limits_are_100_physical_and_10_ebooks()
    {
        var defaults = new SharingLimitsOptions();

        defaults.PhysicalLibrary.Should().Be(100);
        defaults.Ebooks.Should().Be(10);
    }

    [Fact]
    public async Task A_request_must_be_accepted_before_anything_can_be_shared()
    {
        using var db = _db.CreateContext();
        var service = Service(db);
        var sent = await service.SendRequestAsync(_me.Id, "anna");

        var turnedOn = await service.SetSharingAsync(_me.Id, sent.Result.Id, SharingSetting.PhysicalLibrary, true);

        turnedOn.IsSuccess.Should().BeFalse();
        (await service.SharesWithAsync(_me.Id, _anna.Id, SharingSetting.PhysicalLibrary)).Should().BeFalse();
    }

    [Fact]
    public async Task All_four_settings_start_off_when_a_request_is_accepted()
    {
        await BecomeFriendsAsync(_me, _anna);

        using var db = _db.CreateContext();
        var service = Service(db);
        foreach (var setting in new[] { SharingSetting.PhysicalLibrary, SharingSetting.Ebooks })
        {
            (await service.SharesWithAsync(_me.Id, _anna.Id, setting)).Should().BeFalse();
            (await service.SharesWithAsync(_anna.Id, _me.Id, setting)).Should().BeFalse();
        }
    }

    [Fact]
    public async Task Accepting_sets_the_accepted_date()
    {
        var id = await BecomeFriendsAsync(_me, _anna);

        using var db = _db.CreateContext();
        var row = db.Friendships.Single(f => f.Id == id);
        row.Status.Should().Be(FriendshipStatus.Accepted);
        row.AcceptedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Sharing_is_per_direction()
    {
        var id = await BecomeFriendsAsync(_me, _anna);

        using var db = _db.CreateContext();
        var service = Service(db);
        var result = await service.SetSharingAsync(_me.Id, id, SharingSetting.Ebooks, true);

        result.IsSuccess.Should().BeTrue(string.Join(", ", result.ErrorMessages));
        (await service.SharesWithAsync(_me.Id, _anna.Id, SharingSetting.Ebooks)).Should().BeTrue();
        (await service.SharesWithAsync(_anna.Id, _me.Id, SharingSetting.Ebooks)).Should().BeFalse();
        (await service.SharesWithAsync(_me.Id, _anna.Id, SharingSetting.PhysicalLibrary)).Should().BeFalse();
    }

    [Fact]
    public async Task Turning_on_one_more_than_the_limit_is_refused_with_a_message_naming_the_limit()
    {
        var withAnna = await BecomeFriendsAsync(_me, _anna);
        var withErik = await BecomeFriendsAsync(_erik, _me);

        using var db = _db.CreateContext();
        var service = Service(db);
        (await service.SetSharingAsync(_me.Id, withAnna, SharingSetting.Ebooks, true)).IsSuccess.Should().BeTrue();

        var refused = await service.SetSharingAsync(_me.Id, withErik, SharingSetting.Ebooks, true);

        refused.IsSuccess.Should().BeFalse();
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        refused.ErrorMessages.Should().ContainSingle().Which.Should().Contain(EbookLimit.ToString());
        (await service.SharesWithAsync(_me.Id, _erik.Id, SharingSetting.Ebooks)).Should().BeFalse();
    }

    [Fact]
    public async Task The_physical_library_limit_is_counted_separately_from_the_ebook_limit()
    {
        var withAnna = await BecomeFriendsAsync(_me, _anna);
        var withErik = await BecomeFriendsAsync(_me, _erik);
        var withOlle = await BecomeFriendsAsync(_me, _olle);

        using var db = _db.CreateContext();
        var service = Service(db);
        (await service.SetSharingAsync(_me.Id, withAnna, SharingSetting.Ebooks, true)).IsSuccess.Should().BeTrue();
        (await service.SetSharingAsync(_me.Id, withAnna, SharingSetting.PhysicalLibrary, true)).IsSuccess.Should().BeTrue();
        (await service.SetSharingAsync(_me.Id, withErik, SharingSetting.PhysicalLibrary, true)).IsSuccess.Should().BeTrue();

        var refused = await service.SetSharingAsync(_me.Id, withOlle, SharingSetting.PhysicalLibrary, true);

        refused.IsSuccess.Should().BeFalse();
        refused.ErrorMessages.Should().ContainSingle().Which.Should().Contain(PhysicalLimit.ToString());
    }

    [Fact]
    public async Task What_friends_share_with_me_does_not_count_against_my_limit()
    {
        var withAnna = await BecomeFriendsAsync(_me, _anna);
        var withErik = await BecomeFriendsAsync(_me, _erik);

        using var db = _db.CreateContext();
        var service = Service(db);
        (await service.SetSharingAsync(_anna.Id, withAnna, SharingSetting.Ebooks, true)).IsSuccess.Should().BeTrue();

        var mine = await service.SetSharingAsync(_me.Id, withErik, SharingSetting.Ebooks, true);

        mine.IsSuccess.Should().BeTrue(string.Join(", ", mine.ErrorMessages));
    }

    [Fact]
    public async Task Turning_one_off_frees_a_place()
    {
        var withAnna = await BecomeFriendsAsync(_me, _anna);
        var withErik = await BecomeFriendsAsync(_me, _erik);

        using var db = _db.CreateContext();
        var service = Service(db);
        await service.SetSharingAsync(_me.Id, withAnna, SharingSetting.Ebooks, true);
        await service.SetSharingAsync(_me.Id, withAnna, SharingSetting.Ebooks, false);

        var result = await service.SetSharingAsync(_me.Id, withErik, SharingSetting.Ebooks, true);

        result.IsSuccess.Should().BeTrue(string.Join(", ", result.ErrorMessages));
    }

    [Fact]
    public async Task Removing_a_friend_turns_both_directions_off_and_frees_the_places()
    {
        var withAnna = await BecomeFriendsAsync(_me, _anna);
        var withErik = await BecomeFriendsAsync(_me, _erik);

        using (var db = _db.CreateContext())
        {
            var service = Service(db);
            await service.SetSharingAsync(_me.Id, withAnna, SharingSetting.Ebooks, true);
            await service.SetSharingAsync(_anna.Id, withAnna, SharingSetting.PhysicalLibrary, true);

            var removed = await service.RemoveAsync(_anna.Id, withAnna);
            removed.IsSuccess.Should().BeTrue(string.Join(", ", removed.ErrorMessages));
        }

        using (var db = _db.CreateContext())
        {
            var service = Service(db);
            db.Friendships.Any(f => f.Id == withAnna).Should().BeFalse();
            (await service.SharesWithAsync(_me.Id, _anna.Id, SharingSetting.Ebooks)).Should().BeFalse();
            (await service.SharesWithAsync(_anna.Id, _me.Id, SharingSetting.PhysicalLibrary)).Should().BeFalse();
            (await service.SetSharingAsync(_me.Id, withErik, SharingSetting.Ebooks, true)).IsSuccess.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Friends_of_friends_see_nothing()
    {
        var withAnna = await BecomeFriendsAsync(_me, _anna);
        await BecomeFriendsAsync(_anna, _erik);

        using var db = _db.CreateContext();
        var service = Service(db);
        await service.SetSharingAsync(_me.Id, withAnna, SharingSetting.PhysicalLibrary, true);

        (await service.SharesWithAsync(_me.Id, _erik.Id, SharingSetting.PhysicalLibrary)).Should().BeFalse();
    }

    [Fact]
    public async Task A_request_back_to_someone_who_already_asked_accepts_theirs()
    {
        using (var db = _db.CreateContext())
        {
            var service = Service(db);
            await service.SendRequestAsync(_anna.Id, "me");

            var back = await service.SendRequestAsync(_me.Id, "anna");

            back.IsSuccess.Should().BeTrue(string.Join(", ", back.ErrorMessages));
        }

        using (var db = _db.CreateContext())
        {
            var row = db.Friendships.Should().ContainSingle().Subject;
            row.RequesterId.Should().Be(_anna.Id);
            row.Status.Should().Be(FriendshipStatus.Accepted);
        }
    }

    [Fact]
    public async Task A_second_request_to_the_same_person_is_refused()
    {
        using var db = _db.CreateContext();
        var service = Service(db);
        await service.SendRequestAsync(_me.Id, "anna");

        var again = await service.SendRequestAsync(_me.Id, "anna");

        again.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task A_request_to_yourself_or_an_unknown_user_is_refused()
    {
        using var db = _db.CreateContext();
        var service = Service(db);

        (await service.SendRequestAsync(_me.Id, "me")).IsSuccess.Should().BeFalse();
        (await service.SendRequestAsync(_me.Id, "nobody")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Asking_again_after_a_decline_reuses_the_row()
    {
        int id;
        using (var db = _db.CreateContext())
        {
            var service = Service(db);
            id = (await service.SendRequestAsync(_me.Id, "anna")).Result.Id;
            (await service.DeclineAsync(_anna.Id, id)).IsSuccess.Should().BeTrue();
        }

        using (var db = _db.CreateContext())
        {
            db.Friendships.Single().DeclinedAt.Should().NotBeNull();
            var again = await Service(db).SendRequestAsync(_me.Id, "anna");
            again.IsSuccess.Should().BeTrue(string.Join(", ", again.ErrorMessages));
        }

        using (var db = _db.CreateContext())
        {
            var row = db.Friendships.Should().ContainSingle().Subject;
            row.Id.Should().Be(id);
            row.Status.Should().Be(FriendshipStatus.Pending);
            row.DeclinedAt.Should().BeNull();
        }
    }

    [Fact]
    public async Task Only_the_person_asked_can_accept_or_decline()
    {
        using var db = _db.CreateContext();
        var service = Service(db);
        var id = (await service.SendRequestAsync(_me.Id, "anna")).Result.Id;

        (await service.AcceptAsync(_me.Id, id)).IsSuccess.Should().BeFalse();
        (await service.AcceptAsync(_erik.Id, id)).IsSuccess.Should().BeFalse();
        (await service.DeclineAsync(_me.Id, id)).IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Cancelling_a_request_deletes_the_row()
    {
        using (var db = _db.CreateContext())
        {
            var service = Service(db);
            var id = (await service.SendRequestAsync(_me.Id, "anna")).Result.Id;

            (await service.CancelAsync(_anna.Id, id)).IsSuccess.Should().BeFalse("only the requester can cancel");
            (await service.CancelAsync(_me.Id, id)).IsSuccess.Should().BeTrue();
        }

        using (var db = _db.CreateContext())
            db.Friendships.Should().BeEmpty();
    }

    [Fact]
    public async Task A_request_notifies_the_person_asked()
    {
        using (var db = _db.CreateContext())
            await Service(db).SendRequestAsync(_me.Id, "anna");

        using (var db = _db.CreateContext())
        {
            var note = db.NotificationItems.Should().ContainSingle().Subject;
            note.UserId.Should().Be(_anna.Id);
            note.Type.Should().Be(NotificationType.FriendRequestReceived);
            note.Message.Should().Contain("me");
        }
    }

    [Fact]
    public async Task Accepting_notifies_the_requester_and_asks_about_sharing()
    {
        await BecomeFriendsAsync(_me, _anna);

        using var db = _db.CreateContext();
        var note = db.NotificationItems.Should().ContainSingle(n => n.UserId == _me.Id).Subject;
        note.Type.Should().Be(NotificationType.FriendRequestAccepted);
        note.Message.Should().Contain("anna").And.Contain("Do you want to share");
    }

    [Fact]
    public async Task Declining_cancelling_and_removing_send_no_notifications()
    {
        using (var db = _db.CreateContext())
        {
            var service = Service(db);
            var toAnna = (await service.SendRequestAsync(_me.Id, "anna")).Result.Id;
            await service.DeclineAsync(_anna.Id, toAnna);
            var toErik = (await service.SendRequestAsync(_me.Id, "erik")).Result.Id;
            await service.CancelAsync(_me.Id, toErik);
        }
        var withOlle = await BecomeFriendsAsync(_me, _olle);
        using (var db = _db.CreateContext())
            await Service(db).RemoveAsync(_olle.Id, withOlle);

        using (var db = _db.CreateContext())
        {
            db.NotificationItems.Select(n => n.Type).Should().OnlyContain(t =>
                t == NotificationType.FriendRequestReceived || t == NotificationType.FriendRequestAccepted);
        }
    }

    [Fact]
    public async Task The_overview_shows_friends_requests_and_how_much_of_each_limit_is_used()
    {
        var withAnna = await BecomeFriendsAsync(_me, _anna);
        using var db = _db.CreateContext();
        var service = Service(db);
        await service.SetSharingAsync(_me.Id, withAnna, SharingSetting.Ebooks, true);
        await service.SendRequestAsync(_me.Id, "erik");
        await service.SendRequestAsync(_olle.Id, "me");

        var overview = (await service.GetOverviewAsync(_me.Id)).Result;

        overview.Friends.Should().ContainSingle().Which.FriendUserName.Should().Be("anna");
        overview.Friends[0].IShareEbooks.Should().BeTrue();
        overview.Friends[0].AcceptedAt.Should().NotBeNull();
        overview.Sent.Should().ContainSingle().Which.FriendUserName.Should().Be("erik");
        overview.Received.Should().ContainSingle().Which.FriendUserName.Should().Be("olle");
        overview.EbooksUsed.Should().Be(1);
        overview.EbooksLimit.Should().Be(EbookLimit);
        overview.PhysicalLibraryUsed.Should().Be(0);
        overview.PhysicalLibraryLimit.Should().Be(PhysicalLimit);
    }

    [Fact]
    public async Task Search_finds_user_names_that_start_with_the_text_and_leaves_out_me_and_existing_rows()
    {
        using (var db = _db.CreateContext())
        {
            db.Users.AddRange(TestData.User("annika"), TestData.User("hanna"));
            db.SaveChanges();
            await Service(db).SendRequestAsync(_me.Id, "anna");
        }

        using (var db = _db.CreateContext())
        {
            var found = (await Service(db).SearchUsersAsync(_me.Id, "an")).Result;

            found.Select(u => u.UserName).Should().BeEquivalentTo(["annika"]);
        }
    }

    [Fact]
    public async Task Search_still_finds_someone_after_a_decline_since_asking_again_is_allowed()
    {
        using (var db = _db.CreateContext())
        {
            var service = Service(db);
            var toAnna = (await service.SendRequestAsync(_me.Id, "anna")).Result.Id;
            await service.DeclineAsync(_anna.Id, toAnna);
            var fromErik = (await service.SendRequestAsync(_erik.Id, "me")).Result.Id;
            await service.DeclineAsync(_me.Id, fromErik);
        }

        using (var db = _db.CreateContext())
        {
            var service = Service(db);
            (await service.SearchUsersAsync(_me.Id, "anna")).Result.Select(u => u.UserName).Should().Equal("anna");
            (await service.SearchUsersAsync(_me.Id, "erik")).Result.Select(u => u.UserName).Should().Equal("erik");
        }
    }
}
