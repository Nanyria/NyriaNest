namespace NyriaNest.Domain.Enums
{
    /// <summary>
    /// Where a friendship stands (#84). Cancelling a request or removing a friend deletes
    /// the row, so there's no Cancelled or Removed. After a decline the requester may ask
    /// again, which reuses the row and sets it back to Pending.
    /// </summary>
    public enum FriendshipStatus
    {
        /// <summary>Sent, waiting for the addressee to accept or decline.</summary>
        Pending = 0,

        /// <summary>Friends. Nothing is shared until a sharing setting is turned on.</summary>
        Accepted = 1,

        /// <summary>The addressee said no. The requester may send a new request.</summary>
        Declined = 2,
    }
}
