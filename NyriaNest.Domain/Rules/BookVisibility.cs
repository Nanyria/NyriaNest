using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Linq.Expressions;

namespace NyriaNest.Domain.Rules
{
    /// <summary>
    /// Which books a reader can see (#83, #85): the shared library, their own books, and the books
    /// of friends who share them (FriendSharing), but none an admin has marked Lost or Removed.
    /// A visitor (userId null) sees the shared library only.
    /// The Library list, a single book (favorites, reviews) and reading an e-book all use this one rule.
    /// </summary>
    public static class BookVisibility
    {
        // An expression, not a plain method, so EF Core can turn it into SQL and the
        // database does the filtering, both for the list and for a single book.
        // friendships is the Friendships table; it becomes a subquery in the same statement.
        public static Expression<Func<Book, bool>> VisibleTo(string? userId, IQueryable<Friendship> friendships)
        {
            if (userId == null)
                return b => b.AdminStatus == null && b.OwnerId == null;

            // Which sharing setting lets a friend see which kind of book. Physical books (paperbacks
            // and hardcovers, as own books are in BookService) need "Share my physical library".
            // Audiobooks aren't physical and aren't shared. E-books: #87 adds SharingSetting.Ebooks here,
            //   || (b.BookType == BookTypeEnums.EBook && ebookSharers.Contains(b.OwnerId))
            // and flips FriendsBooksVisibilityTests.AFriendWhoSharesEverything_TheirEbooksDontShowYet.
            var physicalSharers = friendships.OwnersSharingWith(userId, SharingSetting.PhysicalLibrary);

            return b => b.AdminStatus == null
                && (b.OwnerId == null
                    || b.OwnerId == userId
                    || ((b.BookType == BookTypeEnums.Paperback || b.BookType == BookTypeEnums.Hardcover)
                        && physicalSharers.Contains(b.OwnerId)));
        }
    }
}
