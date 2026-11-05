using AutoMapper;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NyriaNest.Api.Helpers.Enums;
using NyriaNest.Api.Repositories;
using NyriaNest.Api.Services;
using NyriaNest.Api.Storage;
using NyriaNest.Api.Tests.TestSupport;

namespace NyriaNest.Api.Tests.Services.BookService;

/// <summary>
/// GetBookStatus is a plain function of the book's loan, reservations and admin status,
/// so these tests need no database (#39).
/// </summary>
public class GetBookStatusTests
{
    // GetBookStatus doesn't touch any dependency, so empty mocks are enough.
    // The database context is null on purpose: if the method ever starts using it, these tests will fail loudly.
    private readonly Api.Services.BookService _service = new(
        Mock.Of<IBookRepo>(),
        Mock.Of<IMapper>(),
        Mock.Of<IUserRepo>(),
        dbContext: null!,
        Mock.Of<IFileStorage>(),
        NullLogger<Api.Services.BookService>.Instance,
        Mock.Of<IEpubService>());

    [Theory]
    [InlineData(BookStatusEnum.Lost)]
    [InlineData(BookStatusEnum.Removed)]
    public void AdminStatus_WinsOverAnActiveLoan(BookStatusEnum adminStatus)
    {
        var book = TestData.Book();
        book.CheckedOutBy = TestData.Loan(book, TestData.User());
        book.AdminStatus = adminStatus;

        var result = _service.GetBookStatus(book, BookStatusEnum.CheckedOut);

        result.BookStatus.Should().Be(adminStatus);
    }

    [Fact]
    public void Returned_WithReservations_IsReserved()
    {
        var book = TestData.Book(status: BookStatusEnum.CheckedOut);
        book.Reservations.Add(TestData.Reservation(book, TestData.User("next")));

        var result = _service.GetBookStatus(book, BookStatusEnum.Returned);

        result.BookStatus.Should().Be(BookStatusEnum.Reserved);
    }

    [Fact]
    public void Returned_WithNoReservations_IsAvailable()
    {
        var book = TestData.Book(status: BookStatusEnum.CheckedOut);

        var result = _service.GetBookStatus(book, BookStatusEnum.Returned);

        result.BookStatus.Should().Be(BookStatusEnum.Available);
    }

    [Fact]
    public void CheckedOut_WithALoan_IsCheckedOut()
    {
        var book = TestData.Book();
        book.CheckedOutBy = TestData.Loan(book, TestData.User());

        var result = _service.GetBookStatus(book, BookStatusEnum.CheckedOut);

        result.BookStatus.Should().Be(BookStatusEnum.CheckedOut);
    }
}
