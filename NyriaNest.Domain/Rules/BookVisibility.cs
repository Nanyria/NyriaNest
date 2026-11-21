using NyriaNest.Domain.Entities;
using System.Linq.Expressions;

namespace NyriaNest.Domain.Rules
{
    /// <summary>
    /// Which books a reader can see (#83): the shared library and their own books, but
    /// none an admin has marked Lost or Removed. A visitor (userId null) sees the shared
    /// library only. Friends' books join here later (#84/#85).
    /// The Library list, favorites and a book's reviews all use this one rule.
    /// </summary>
    public static class BookVisibility
    {
        // An expression, not a plain method, so EF Core can turn it into SQL and the
        // database does the filtering, both for the list and for a single book
        public static Expression<Func<Book, bool>> VisibleTo(string? userId) =>
            b => b.AdminStatus == null && (b.OwnerId == null || b.OwnerId == userId);
    }
}
