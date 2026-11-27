using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NyriaNest.Application.Common;
using NyriaNest.Application.DTOs.Friends;
using NyriaNest.Application.Interfaces;
using NyriaNest.Application.Loans;
using NyriaNest.Application.Notifications;
using NyriaNest.Domain.Entities;
using NyriaNest.Domain.Enums;
using static NyriaNest.Domain.Rules.FriendSharing;

namespace NyriaNest.Application.Friends
{
    /// <summary>
    /// Friend requests and the sharing settings (#84, ADR 0005). One row per pair of users; a
    /// request must be accepted before anything can be shared, sharing is per direction, and the
    /// user's own settings that are on count against the limits. Friends of friends see nothing.
    /// </summary>
    public interface IFriendshipService
    {
        /// <summary>
        /// Sends a request to the user with that user name. If they already asked me, this accepts
        /// their request instead. After a decline the same row is reused. Unknown user: 404.
        /// </summary>
        Task<APIResponse<FriendshipDto>> SendRequestAsync(string userId, string userName);

        /// <summary>The person asked accepts. Starts with nothing shared; the requester is notified.</summary>
        Task<APIResponse<FriendshipDto>> AcceptAsync(string userId, int friendshipId);

        /// <summary>The person asked declines. Nobody is notified; the requester may ask again.</summary>
        Task<APIResponse<FriendshipDto>> DeclineAsync(string userId, int friendshipId);

        /// <summary>The requester takes back a pending request. Deletes the row.</summary>
        Task<APIResponse<bool>> CancelAsync(string userId, int friendshipId);

        /// <summary>Either friend ends the friendship. Deletes the row, so both directions stop at once.</summary>
        Task<APIResponse<bool>> RemoveAsync(string userId, int friendshipId);

        /// <summary>Turns one of my sharing settings for this friend on or off. Turning on past the limit: 400.</summary>
        Task<APIResponse<FriendshipDto>> SetSharingAsync(string userId, int friendshipId, SharingSetting setting, bool on);

        /// <summary>Whether <paramref name="ownerId"/> shares this with <paramref name="viewerId"/> (for #85 / #87).</summary>
        Task<bool> SharesWithAsync(string ownerId, string viewerId, SharingSetting setting);

        /// <summary>My friends, requests sent and received, and how much of each limit is used.</summary>
        Task<APIResponse<FriendsOverviewDto>> GetOverviewAsync(string userId);

        /// <summary>Users whose user name starts with the text, leaving out me, my friends and open requests either way.</summary>
        Task<APIResponse<List<UserSearchResultDto>>> SearchUsersAsync(string userId, string text);
    }

    public class FriendshipService : IFriendshipService
    {
        private const int MaxSearchResults = 20;

        private readonly IAppDbContext _dbContext;
        private readonly IUserRepo _userRepo;
        private readonly INotificationService _notifications;
        private readonly SharingLimitsOptions _limits;
        private readonly IAvailabilityService _availability;

        public FriendshipService(IAppDbContext dbContext, IUserRepo userRepo,
            INotificationService notifications, IOptions<SharingLimitsOptions> limits,
            IAvailabilityService availability)
        {
            _dbContext = dbContext;
            _userRepo = userRepo;
            _notifications = notifications;
            _limits = limits.Value;
            _availability = availability;
        }

        public async Task<APIResponse<FriendshipDto>> SendRequestAsync(string userId, string userName)
        {
            var normalized = (userName ?? "").Trim().ToUpperInvariant();
            var other = await _userRepo.FindByCondition<User>(u => u.NormalizedUserName == normalized)
                .Select(u => new { u.Id, u.UserName })
                .FirstOrDefaultAsync();
            if (other is null)
                return Fail<FriendshipDto>(HttpStatusCode.NotFound, $"There's no user named \"{userName}\".");
            if (other.Id == userId)
                return Fail<FriendshipDto>(HttpStatusCode.BadRequest, "You can't send a friend request to yourself.");

            var myUserName = await UserNameOfAsync(userId);
            var row = await _dbContext.Friendships.FirstOrDefaultAsync(Between(userId, other.Id));

            if (row is null)
            {
                row = new Friendship { RequesterId = userId, AddresseeId = other.Id, RequestedAt = DateTime.UtcNow };
                _dbContext.Friendships.Add(row);
            }
            else if (row.Status == FriendshipStatus.Accepted)
            {
                return Fail<FriendshipDto>(HttpStatusCode.BadRequest, $"You're already friends with {other.UserName}.");
            }
            else if (row.Status == FriendshipStatus.Pending && row.RequesterId == userId)
            {
                return Fail<FriendshipDto>(HttpStatusCode.BadRequest, $"You've already sent {other.UserName} a request.");
            }
            else if (row.Status == FriendshipStatus.Pending)
            {
                // They already asked me: answering with a request means yes
                Accept(row, myUserName);
                await _dbContext.SaveChangesAsync();
                return Ok(ToDto(row, userId, other.UserName));
            }
            else
            {
                // Declined before: ask again on the same row, with whoever asks now as the requester
                row.RequesterId = userId;
                row.AddresseeId = other.Id;
                row.Status = FriendshipStatus.Pending;
                row.RequestedAt = DateTime.UtcNow;
                row.DeclinedAt = null;
            }

            _notifications.NotifyFriendRequestReceived(other.Id, myUserName);
            await _dbContext.SaveChangesAsync();
            return Ok(ToDto(row, userId, other.UserName));
        }

        public async Task<APIResponse<FriendshipDto>> AcceptAsync(string userId, int friendshipId)
        {
            var row = await PendingRequestToAsync(userId, friendshipId);
            if (row is null)
                return Fail<FriendshipDto>(HttpStatusCode.NotFound, "That friend request wasn't found.");

            Accept(row, row.Addressee.UserName ?? "");
            await _dbContext.SaveChangesAsync();
            return Ok(ToDto(row, userId, row.Requester.UserName));
        }

        public async Task<APIResponse<FriendshipDto>> DeclineAsync(string userId, int friendshipId)
        {
            var row = await PendingRequestToAsync(userId, friendshipId);
            if (row is null)
                return Fail<FriendshipDto>(HttpStatusCode.NotFound, "That friend request wasn't found.");

            row.Status = FriendshipStatus.Declined;
            row.DeclinedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();
            return Ok(ToDto(row, userId, row.Requester.UserName));
        }

        public async Task<APIResponse<bool>> CancelAsync(string userId, int friendshipId)
        {
            var row = await _dbContext.Friendships.FirstOrDefaultAsync(f =>
                f.Id == friendshipId && f.RequesterId == userId && f.Status == FriendshipStatus.Pending);
            if (row is null)
                return Fail<bool>(HttpStatusCode.NotFound, "That friend request wasn't found.");

            _dbContext.Friendships.Remove(row);
            await _dbContext.SaveChangesAsync();
            return Ok(true);
        }

        public async Task<APIResponse<bool>> RemoveAsync(string userId, int friendshipId)
        {
            var row = await _dbContext.Friendships
                .Include(f => f.Requester)
                .Include(f => f.Addressee)
                .FirstOrDefaultAsync(f =>
                    f.Id == friendshipId && (f.RequesterId == userId || f.AddresseeId == userId)
                    && f.Status == FriendshipStatus.Accepted);
            if (row is null)
                return Fail<bool>(HttpStatusCode.NotFound, "That friend wasn't found.");

            // Deleting the row turns off all four settings and frees the places at once (ADR 0005).
            // #85: neither can see the other's books any more, so what each had open for them goes.
            await EndAccessToBooksAsync(row.Requester, row.AddresseeId);
            await EndAccessToBooksAsync(row.Addressee, row.RequesterId);
            _dbContext.Friendships.Remove(row);
            await _dbContext.SaveChangesAsync();
            return Ok(true);
        }

        public async Task<APIResponse<FriendshipDto>> SetSharingAsync(string userId, int friendshipId, SharingSetting setting, bool on)
        {
            var row = await _dbContext.Friendships
                .Include(f => f.Requester)
                .Include(f => f.Addressee)
                .FirstOrDefaultAsync(f =>
                    f.Id == friendshipId && (f.RequesterId == userId || f.AddresseeId == userId)
                    && f.Status == FriendshipStatus.Accepted);
            if (row is null)
                return Fail<FriendshipDto>(HttpStatusCode.NotFound, "That friend wasn't found. Sharing needs an accepted friend request.");

            var iAmRequester = row.RequesterId == userId;
            var friendUserName = iAmRequester ? row.Addressee.UserName : row.Requester.UserName;

            if (on && !IShare(row, userId, setting))
            {
                var limit = LimitFor(setting);
                var used = await _dbContext.Friendships.CountAsync(SharedBy(userId, setting));
                if (used >= limit)
                {
                    var what = setting == SharingSetting.Ebooks ? "your e-books" : "your physical library";
                    return Fail<FriendshipDto>(HttpStatusCode.BadRequest,
                        $"You already share {what} with {limit} friends, which is the limit. Turn it off for someone else first.");
                }
            }

            switch (setting, iAmRequester)
            {
                case (SharingSetting.PhysicalLibrary, true): row.RequesterSharesPhysicalLibrary = on; break;
                case (SharingSetting.Ebooks, true): row.RequesterSharesEbooks = on; break;
                case (SharingSetting.PhysicalLibrary, false): row.AddresseeSharesPhysicalLibrary = on; break;
                case (SharingSetting.Ebooks, false): row.AddresseeSharesEbooks = on; break;
            }

            // #85: the friend can't see my physical books any more, so their requests and reservations go
            if (setting == SharingSetting.PhysicalLibrary && !on)
                await EndAccessToBooksAsync(iAmRequester ? row.Requester : row.Addressee,
                    iAmRequester ? row.AddresseeId : row.RequesterId);

            await _dbContext.SaveChangesAsync();
            return Ok(ToDto(row, userId, friendUserName));
        }

        public Task<bool> SharesWithAsync(string ownerId, string viewerId, SharingSetting setting)
        {
            // Only the row between these two counts, so a friend's friends never get anything
            return _dbContext.Friendships
                .Where(Between(ownerId, viewerId))
                .AnyAsync(SharedBy(ownerId, setting));
        }

        public async Task<APIResponse<FriendsOverviewDto>> GetOverviewAsync(string userId)
        {
            var rows = await _dbContext.Friendships
                .AsNoTracking()
                .Where(f => f.RequesterId == userId || f.AddresseeId == userId)
                .Select(f => new
                {
                    Row = f,
                    RequesterName = f.Requester.UserName,
                    AddresseeName = f.Addressee.UserName,
                    RequesterPicture = f.Requester.ProfilePictureUrl,
                    AddresseePicture = f.Addressee.ProfilePictureUrl,
                })
                .ToListAsync();

            var dtos = rows
                .Select(r =>
                {
                    var iAmRequester = r.Row.RequesterId == userId;
                    var dto = ToDto(r.Row, userId, iAmRequester ? r.AddresseeName : r.RequesterName);
                    dto.FriendProfilePictureUrl = iAmRequester ? r.AddresseePicture : r.RequesterPicture;
                    return dto;
                })
                .ToList();
            var friends = dtos.Where(d => d.Status == FriendshipStatus.Accepted).ToList();

            return Ok(new FriendsOverviewDto
            {
                Friends = friends.OrderBy(d => d.FriendUserName).ToList(),
                Sent = dtos.Where(d => d.Status == FriendshipStatus.Pending && d.SentByMe)
                    .OrderByDescending(d => d.RequestedAt).ToList(),
                Received = dtos.Where(d => d.Status == FriendshipStatus.Pending && !d.SentByMe)
                    .OrderByDescending(d => d.RequestedAt).ToList(),
                PhysicalLibraryUsed = friends.Count(d => d.ISharePhysicalLibrary),
                PhysicalLibraryLimit = _limits.PhysicalLibrary,
                EbooksUsed = friends.Count(d => d.IShareEbooks),
                EbooksLimit = _limits.Ebooks,
            });
        }

        public async Task<APIResponse<List<UserSearchResultDto>>> SearchUsersAsync(string userId, string text)
        {
            var prefix = (text ?? "").Trim().ToUpperInvariant();
            if (prefix.Length == 0)
                return Ok(new List<UserSearchResultDto>());

            // Friends and open requests either way. Declined ones still show up, since asking again is allowed.
            var taken = _dbContext.Friendships
                .Where(f => f.RequesterId == userId || f.AddresseeId == userId)
                .Where(f => f.Status != FriendshipStatus.Declined)
                .Select(f => f.RequesterId == userId ? f.AddresseeId : f.RequesterId);

            var found = await _userRepo.FindByCondition<User>(u =>
                    u.Id != userId && u.NormalizedUserName != null && u.NormalizedUserName.StartsWith(prefix))
                .Where(u => !taken.Contains(u.Id))
                .OrderBy(u => u.UserName)
                .Take(MaxSearchResults)
                .Select(u => new UserSearchResultDto { UserId = u.Id, UserName = u.UserName ?? "" })
                .ToListAsync();

            return Ok(found);
        }

        // ---- Helpers ----

        private void Accept(Friendship row, string accepterUserName)
        {
            row.Status = FriendshipStatus.Accepted;
            row.AcceptedAt = DateTime.UtcNow;
            _notifications.NotifyFriendRequestAccepted(row.RequesterId, accepterUserName);
        }

        private Task<Friendship?> PendingRequestToAsync(string userId, int friendshipId) =>
            _dbContext.Friendships
                .Include(f => f.Requester)
                .Include(f => f.Addressee)
                .FirstOrDefaultAsync(f =>
                    f.Id == friendshipId && f.AddresseeId == userId && f.Status == FriendshipStatus.Pending);

        private async Task<string> UserNameOfAsync(string userId) =>
            await _userRepo.FindByCondition<User>(u => u.Id == userId).Select(u => u.UserName).FirstOrDefaultAsync() ?? "";

        private int LimitFor(SharingSetting setting) =>
            setting == SharingSetting.Ebooks ? _limits.Ebooks : _limits.PhysicalLibrary;

        /// <summary>
        /// #85: the friend can no longer see the owner's physical books. Their open requests for them
        /// are cancelled and their reservations removed (the rest of each queue moves up); a loan they
        /// already have stays until it's returned. The friend is told once, if anything went. Doesn't save.
        /// </summary>
        private async Task EndAccessToBooksAsync(User owner, string friendId)
        {
            var requests = await _dbContext.BorrowRequests
                .Where(r => r.BorrowerId == friendId && r.Book.OwnerId == owner.Id && r.Status == BorrowRequestStatus.Requested)
                .ToListAsync();
            var now = DateTime.UtcNow;
            foreach (var request in requests)
            {
                request.Status = BorrowRequestStatus.Cancelled;
                request.AnsweredAt = now;
            }

            var queuedBooks = await _dbContext.Books
                .Include(b => b.Reservations)
                .Include(b => b.CheckedOutBy)
                .Where(b => b.OwnerId == owner.Id && b.Reservations.Any(r => r.UserId == friendId))
                .ToListAsync();
            foreach (var book in queuedBooks)
            {
                foreach (var reservation in book.Reservations.Where(r => r.UserId == friendId).ToList())
                {
                    book.Reservations.Remove(reservation);
                    _dbContext.ReservationItems.Remove(reservation);
                }
                _availability.RecalculateAvailabilityDates(book);
            }

            if (requests.Count > 0 || queuedBooks.Count > 0)
                _notifications.NotifySharingEnded(friendId, owner.UserName ?? "");
        }

        // Between and SharedBy live in Domain (FriendSharing) since #85, shared with BookVisibility

        private static bool IShare(Friendship f, string userId, SharingSetting setting) =>
            (setting, f.RequesterId == userId) switch
            {
                (SharingSetting.PhysicalLibrary, true) => f.RequesterSharesPhysicalLibrary,
                (SharingSetting.Ebooks, true) => f.RequesterSharesEbooks,
                (SharingSetting.PhysicalLibrary, false) => f.AddresseeSharesPhysicalLibrary,
                _ => f.AddresseeSharesEbooks,
            };

        private static FriendshipDto ToDto(Friendship f, string userId, string? friendUserName)
        {
            var iAmRequester = f.RequesterId == userId;
            return new FriendshipDto
            {
                Id = f.Id,
                FriendUserId = iAmRequester ? f.AddresseeId : f.RequesterId,
                FriendUserName = friendUserName ?? "",
                Status = f.Status,
                SentByMe = iAmRequester,
                RequestedAt = f.RequestedAt,
                AcceptedAt = f.AcceptedAt,
                ISharePhysicalLibrary = iAmRequester ? f.RequesterSharesPhysicalLibrary : f.AddresseeSharesPhysicalLibrary,
                IShareEbooks = iAmRequester ? f.RequesterSharesEbooks : f.AddresseeSharesEbooks,
                FriendSharesPhysicalLibrary = iAmRequester ? f.AddresseeSharesPhysicalLibrary : f.RequesterSharesPhysicalLibrary,
                FriendSharesEbooks = iAmRequester ? f.AddresseeSharesEbooks : f.RequesterSharesEbooks,
            };
        }

        private static APIResponse<T> Ok<T>(T result) => new()
        {
            IsSuccess = true,
            StatusCode = HttpStatusCode.OK,
            Result = result,
        };

        private static APIResponse<T> Fail<T>(HttpStatusCode status, string message) => new()
        {
            IsSuccess = false,
            StatusCode = status,
            ErrorMessages = [message],
        };
    }
}
