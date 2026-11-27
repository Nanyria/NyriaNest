using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using System.Linq.Expressions;

namespace NyriaNest.Domain.Rules
{
    /// <summary>
    /// What "a friend shares X with me" means (#84, ADR 0005): an accepted friendship where the
    /// owner's own setting for X is on. Only the row between the two counts, so a friend's friends
    /// get nothing. FriendshipService (the sharing checks and limits) and BookVisibility (which
    /// books show) both use these, so the meaning can't drift apart.
    /// Expressions, not plain methods, so EF Core turns them into SQL.
    /// </summary>
    public static class FriendSharing
    {
        /// <summary>The row between two users, whichever of them asked.</summary>
        public static Expression<Func<Friendship, bool>> Between(string a, string b) =>
            f => (f.RequesterId == a && f.AddresseeId == b) || (f.RequesterId == b && f.AddresseeId == a);

        /// <summary>
        /// Accepted rows where <paramref name="sharerId"/> has this setting on, with anyone.
        /// Counted for the sharing limits; with <see cref="Between"/> it answers "shares with this friend".
        /// </summary>
        public static Expression<Func<Friendship, bool>> SharedBy(string sharerId, SharingSetting setting) =>
            setting == SharingSetting.Ebooks
                ? f => f.Status == FriendshipStatus.Accepted
                    && ((f.RequesterId == sharerId && f.RequesterSharesEbooks) || (f.AddresseeId == sharerId && f.AddresseeSharesEbooks))
                : f => f.Status == FriendshipStatus.Accepted
                    && ((f.RequesterId == sharerId && f.RequesterSharesPhysicalLibrary) || (f.AddresseeId == sharerId && f.AddresseeSharesPhysicalLibrary));

        /// <summary>
        /// The users who share this setting with <paramref name="viewerId"/>: the friend on the
        /// other side of each accepted row where the friend's own setting is on. A subquery, so the
        /// Library can ask "is the owner one of these?" in the same SQL statement.
        /// </summary>
        public static IQueryable<string> OwnersSharingWith(this IQueryable<Friendship> friendships,
            string viewerId, SharingSetting setting) =>
            friendships
                .Where(setting == SharingSetting.Ebooks
                    ? f => f.Status == FriendshipStatus.Accepted
                        && ((f.AddresseeId == viewerId && f.RequesterSharesEbooks) || (f.RequesterId == viewerId && f.AddresseeSharesEbooks))
                    : f => f.Status == FriendshipStatus.Accepted
                        && ((f.AddresseeId == viewerId && f.RequesterSharesPhysicalLibrary) || (f.RequesterId == viewerId && f.AddresseeSharesPhysicalLibrary)))
                .Select(f => f.AddresseeId == viewerId ? f.RequesterId : f.AddresseeId);
    }
}
