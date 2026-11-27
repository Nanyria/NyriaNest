using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Net;
using System.Net.Http.Json;

namespace NyriaNest.Api.Tests.Controllers.Books;

/// <summary>
/// #83 step 4a: an owner lends their own book to a name (someone without an account) and
/// marks it returned, through api/my/books/{id}/lend and /return. Both write one history row.
/// Lending to a member (a user id) is refused until friends exist (#85). The due date is
/// lend date + 14 days for now; choosing it is #133. Dates may be in the past, never the future,
/// and a chosen date goes in the history note as yyyy-MM-dd.
/// </summary>
public class MyBookLendingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    // ---- Lend ----

    [Fact]
    public async Task Lend_ToAName_SavesTheLoan_MarksTheBookLent_AndRecordsIt()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(me.Id);

        var response = await LendAsync(client, bookId, new { borrowerName = "Ruth (neighbour)" });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var loan = await LoanOf(bookId);
        loan.BorrowerName.Should().Be("Ruth (neighbour)");
        loan.UserId.Should().BeNull();
        loan.CheckOutDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        loan.ReturnDate.Should().Be(loan.CheckOutDate.AddDays(14));
        var book = await BookAsync(bookId);
        book.BookStatus.Should().Be(BookStatusEnum.CheckedOut);
        book.AvailabilityDate.Should().Be(loan.ReturnDate);
        var history = await HistoryOf(bookId);
        history.Should().ContainSingle();
        history[0].BookStatus.Should().Be(BookStatusEnum.CheckedOut);
        history[0].UserId.Should().Be(me.Id);
        history[0].Notes.Should().Contain("Ruth (neighbour)");
    }

    [Fact]
    public async Task Lend_TrimsTheName()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(me.Id);

        var response = await LendAsync(client, bookId, new { borrowerName = "  Ruth  " });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await LoanOf(bookId)).BorrowerName.Should().Be("Ruth");
    }

    [Fact]
    public async Task Lend_ANameOf100Characters_IsAccepted()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(me.Id);

        var response = await LendAsync(client, bookId, new { borrowerName = new string('a', 100) });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Lend_WithAnEarlierDate_UsesIt_AndNotesIt()
    {
        // Recording a loan after the fact: lent two weeks ago, added to NyriaNest today
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(me.Id);
        var lentOn = Today.AddDays(-14);

        var response = await LendAsync(client, bookId, new { borrowerName = "Ruth", lentOn });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        var loan = await LoanOf(bookId);
        DateOnly.FromDateTime(loan.CheckOutDate).Should().Be(lentOn);
        loan.ReturnDate.Should().Be(loan.CheckOutDate.AddDays(14));
        (await HistoryOf(bookId)).Single().Notes.Should().Contain(lentOn.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task Lend_WithAFutureDate_Returns400_AndLendsNothing()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(me.Id);

        var response = await LendAsync(client, bookId, new { borrowerName = "Ruth", lentOn = Today.AddDays(1) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LoanCountOf(bookId)).Should().Be(0);
    }

    [Theory]
    [InlineData("member")]   // a user id only: members come with friends (#85)
    [InlineData("both")]     // a loan goes to one person
    [InlineData("neither")]  // nobody to lend to
    [InlineData("blank")]    // empty once trimmed
    [InlineData("tooLong")]  // longer than the 100-character column
    public async Task Lend_WithoutExactlyOneValidName_Returns400_AndLendsNothing(string borrower)
    {
        var (client, me) = await factory.LoginAsAsync();
        var member = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(me.Id);
        object body = borrower switch
        {
            "member" => new { borrowerUserId = member.Id },
            "both" => new { borrowerUserId = member.Id, borrowerName = "Ruth" },
            "neither" => new { },
            "blank" => new { borrowerName = "   " },
            "tooLong" => new { borrowerName = new string('a', 101) },
            _ => throw new ArgumentOutOfRangeException(nameof(borrower)),
        };

        var response = await LendAsync(client, bookId, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LoanCountOf(bookId)).Should().Be(0);
        (await BookAsync(bookId)).BookStatus.Should().Be(BookStatusEnum.Available);
    }

    [Fact]
    public async Task Lend_ABookThatIsAlreadyLent_Returns409_AndKeepsTheFirstLoan()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddLentBookAsync(me.Id, "Ruth");

        var response = await LendAsync(client, bookId, new { borrowerName = "Sam" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await LoanOf(bookId)).BorrowerName.Should().Be("Ruth");
    }

    // ---- Return ----

    [Fact]
    public async Task Return_WithoutABody_RemovesTheLoan_MarksTheBookAvailable_AndRecordsOneRow()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddLentBookAsync(me.Id, "Ruth (neighbour)");

        var response = await client.PostAsync($"api/my/books/{bookId}/return", null, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await LoanCountOf(bookId)).Should().Be(0);
        var book = await BookAsync(bookId);
        book.BookStatus.Should().Be(BookStatusEnum.Available);
        book.AvailabilityDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1)); // available from now, as when added
        var history = await HistoryOf(bookId);
        history.Should().ContainSingle(); // "Returned" only, no extra "Available" row
        history[0].BookStatus.Should().Be(BookStatusEnum.Returned);
        history[0].UserId.Should().Be(me.Id);
        history[0].Notes.Should().Contain("Ruth (neighbour)");
    }

    [Fact]
    public async Task Return_WithAnEarlierDate_NotesIt()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddLentBookAsync(me.Id, "Ruth", lentDaysAgo: 30);
        var returnedOn = Today.AddDays(-7);

        var response = await ReturnAsync(client, bookId, new { returnedOn });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await HistoryOf(bookId)).Single().Notes.Should().Contain(returnedOn.ToString("yyyy-MM-dd"));
    }

    [Fact]
    public async Task Return_OnTheDayItWasLent_IsAccepted()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddLentBookAsync(me.Id, "Ruth", lentDaysAgo: 3);

        var response = await ReturnAsync(client, bookId, new { returnedOn = Today.AddDays(-3) });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData(1)]   // tomorrow
    [InlineData(-10)] // before it was lent (3 days ago)
    public async Task Return_WithAnImpossibleDate_Returns400_AndKeepsTheLoan(int daysFromToday)
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddLentBookAsync(me.Id, "Ruth", lentDaysAgo: 3);

        var response = await ReturnAsync(client, bookId, new { returnedOn = Today.AddDays(daysFromToday) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LoanCountOf(bookId)).Should().Be(1);
        (await HistoryOf(bookId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Return_ABookThatIsntLent_Returns409()
    {
        var (client, me) = await factory.LoginAsAsync();
        var bookId = await AddBookAsync(me.Id);

        var response = await ReturnAsync(client, bookId, new { });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await HistoryOf(bookId)).Should().BeEmpty();
    }

    [Fact]
    public async Task Return_ABookLentToAMember_ConfirmsTheReturn()
    {
        // #85: was 409 ("#85 decides how they end"). The owner's word is enough, with or without
        // the friend's report; the details are in FriendReturnTests.
        var (client, me) = await factory.LoginAsAsync();
        var member = await factory.CreateUserAsync();
        var bookId = await AddBookAsync(me.Id, BookStatusEnum.CheckedOut);
        await AddLoanAsync(bookId, userId: member.Id);

        var response = await ReturnAsync(client, bookId, new { });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        (await LoanCountOf(bookId)).Should().Be(0);
    }

    // ---- Not mine: someone else's book or a shared-library book looks missing ----

    public static TheoryData<bool> NotMine => new() { true, false }; // true = another user's, false = shared library

    [Theory]
    [MemberData(nameof(NotMine))]
    public async Task Lend_ABookThatIsntMine_Returns404_AndLendsNothing(bool ownedBySomeoneElse)
    {
        var (client, _) = await factory.LoginAsAsync();
        var bookId = await AddNotMineAsync(ownedBySomeoneElse);

        var response = await LendAsync(client, bookId, new { borrowerName = "Ruth" });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await LoanCountOf(bookId)).Should().Be(0);
    }

    [Fact]
    public async Task Return_SomeoneElsesLentBook_Returns404_AndKeepsTheLoan()
    {
        var (client, _) = await factory.LoginAsAsync();
        var owner = await factory.CreateUserAsync();
        var bookId = await AddLentBookAsync(owner.Id, "Ruth");

        var response = await ReturnAsync(client, bookId, new { });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await LoanCountOf(bookId)).Should().Be(1);
    }

    [Theory]
    [InlineData("lend")]
    [InlineData("return")]
    public async Task LendOrReturn_WithoutLogin_Returns401(string action)
    {
        var response = await factory.CreateClient().PostAsJsonAsync(
            $"api/my/books/1004/{action}", new { borrowerName = "Ruth" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- Helpers ----

    private Task<HttpResponseMessage> LendAsync(HttpClient client, int bookId, object body) =>
        client.PostAsJsonAsync($"api/my/books/{bookId}/lend", body, Ct);

    private Task<HttpResponseMessage> ReturnAsync(HttpClient client, int bookId, object body) =>
        client.PostAsJsonAsync($"api/my/books/{bookId}/return", body, Ct);

    private Task<int> AddBookAsync(string? ownerId, BookStatusEnum status = BookStatusEnum.Available) =>
        factory.WithDbAsync(async db =>
        {
            var book = TestData.Book("Lendable", status: status);
            book.OwnerId = ownerId;
            db.Books.Add(book);
            await db.SaveChangesAsync(Ct);
            return book.BookId;
        });

    private async Task<int> AddNotMineAsync(bool ownedBySomeoneElse)
    {
        var owner = ownedBySomeoneElse ? (await factory.CreateUserAsync()).Id : null;
        return await AddBookAsync(owner);
    }

    private async Task<int> AddLentBookAsync(string ownerId, string borrowerName, int lentDaysAgo = 0)
    {
        var bookId = await AddBookAsync(ownerId, BookStatusEnum.CheckedOut);
        await AddLoanAsync(bookId, borrowerName: borrowerName, lentDaysAgo: lentDaysAgo);
        return bookId;
    }

    private Task AddLoanAsync(int bookId, string? borrowerName = null, string? userId = null, int lentDaysAgo = 0) =>
        factory.WithDbAsync(async db =>
        {
            var lentAt = DateTime.UtcNow.AddDays(-lentDaysAgo);
            db.CheckOutItems.Add(new CheckedOutItem
            {
                BookId = bookId,
                BorrowerName = borrowerName,
                UserId = userId,
                CheckOutDate = lentAt,
                ReturnDate = lentAt.AddDays(14),
            });
            await db.SaveChangesAsync(Ct);
        });

    private Task<Book> BookAsync(int bookId) => factory.WithDbAsync(db =>
        db.Books.AsNoTracking().SingleAsync(b => b.BookId == bookId, Ct));

    private Task<CheckedOutItem> LoanOf(int bookId) => factory.WithDbAsync(db =>
        db.CheckOutItems.AsNoTracking().SingleAsync(l => l.BookId == bookId, Ct));

    private Task<int> LoanCountOf(int bookId) => factory.WithDbAsync(db =>
        db.CheckOutItems.CountAsync(l => l.BookId == bookId, Ct));

    private Task<List<StatusHistoryItem>> HistoryOf(int bookId) => factory.WithDbAsync(db =>
        db.StatusHistoryItems.AsNoTracking().Where(h => h.BookId == bookId).ToListAsync(Ct));
}
