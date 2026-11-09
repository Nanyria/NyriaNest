using AwesomeAssertions;
using Moq;
using NyriaNest.Api.Tests.TestSupport;
using NyriaNest.Application.Interfaces;
using NyriaNest.Application.Loans;
using NyriaNest.Domain.Enums;

namespace NyriaNest.Api.Tests.Application.Loans;

/// <summary>
/// #39 part B: each reader's expected date in the queue. First come, first served:
/// the first in line gets the book when the loan is due (or today, if nobody has it),
/// and each one after waits one more loan period.
/// </summary>
public class RecalculateAvailabilityDatesTests
{
    private static readonly int Period = AvailabilityService.LoanPeriodMonths;

    // Like GetBookStatusTests: the method only works on the book it's given
    private readonly AvailabilityService _service = new(
        Mock.Of<IUserRepo>(),
        Mock.Of<IBookRepo>(),
        dbContext: null!);

    [Fact]
    public void WithALoan_TheQueueStartsAtTheDueDate_SpacedByTheLoanPeriod_InReservationOrder()
    {
        var book = TestData.Book(status: BookStatusEnum.CheckedOut);
        book.CheckedOutBy = TestData.Loan(book, TestData.User("holder"), dueInDays: 10);
        var due = book.CheckedOutBy.ReturnDate;
        // Added out of order: the queue must follow the reservation dates
        var third = TestData.Reservation(book, TestData.User("third"), daysAgo: 1);
        var first = TestData.Reservation(book, TestData.User("first"), daysAgo: 3);
        var second = TestData.Reservation(book, TestData.User("second"), daysAgo: 2);
        book.Reservations.AddRange([third, first, second]);

        _service.RecalculateAvailabilityDates(book);

        first.AvailabilityDate.Should().Be(due);
        second.AvailabilityDate.Should().Be(due.AddMonths(Period));
        third.AvailabilityDate.Should().Be(due.AddMonths(2 * Period));
    }

    [Fact]
    public void WithoutALoan_TheFirstInLineCanHaveItToday()
    {
        var book = TestData.Book(status: BookStatusEnum.Reserved);
        var first = TestData.Reservation(book, TestData.User("first"), daysAgo: 2);
        var second = TestData.Reservation(book, TestData.User("second"), daysAgo: 1);
        book.Reservations.AddRange([first, second]);

        _service.RecalculateAvailabilityDates(book);

        first.AvailabilityDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        second.AvailabilityDate.Should().Be(first.AvailabilityDate!.Value.AddMonths(Period));
    }

    [Fact]
    public void TheBooksOwnDate_IsWhenTheLastInLineGetsIt()
    {
        var book = TestData.Book(status: BookStatusEnum.CheckedOut);
        book.CheckedOutBy = TestData.Loan(book, TestData.User("holder"), dueInDays: 10);
        book.Reservations.AddRange([
            TestData.Reservation(book, TestData.User("first"), daysAgo: 2),
            TestData.Reservation(book, TestData.User("second"), daysAgo: 1),
        ]);

        _service.RecalculateAvailabilityDates(book);

        book.AvailabilityDate.Should().Be(book.CheckedOutBy.ReturnDate.AddMonths(Period));
    }

    [Fact]
    public void WithALoanAndNoQueue_TheBooksOwnDate_IsTheDueDate()
    {
        var book = TestData.Book(status: BookStatusEnum.CheckedOut);
        book.CheckedOutBy = TestData.Loan(book, TestData.User("holder"), dueInDays: 10);

        _service.RecalculateAvailabilityDates(book);

        book.AvailabilityDate.Should().Be(book.CheckedOutBy.ReturnDate);
    }

    [Fact]
    public void WithNoLoanAndNoQueue_TheBookIsAvailableToday()
    {
        var book = TestData.Book();

        _service.RecalculateAvailabilityDates(book);

        book.AvailabilityDate.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }
}
