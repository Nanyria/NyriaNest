namespace NyriaNest.Domain.Enums
{
    /// <summary>
    /// Where a friend's request to borrow a book stands (#85). Only Requested is open;
    /// the others are finished and stay as the record. Saved as a number: never change
    /// or reuse a number, only add new states at the end.
    /// </summary>
    public enum BorrowRequestStatus
    {
        /// <summary>Waiting for the owner to accept or decline.</summary>
        Requested = 0,

        /// <summary>The owner accepted and lent the book (a normal CheckedOut loan).</summary>
        Lent = 1,

        /// <summary>The owner said no.</summary>
        Declined = 2,

        /// <summary>The friend withdrew it, or an admin cancelled it for support (#50).</summary>
        Cancelled = 3,

        /// <summary>The owner didn't answer within RequestExpiryMonths. The friend is told and can ask again.</summary>
        Expired = 4,

        /// <summary>The owner lent the book to another friend; this request became a reservation in the queue.</summary>
        Queued = 5,
    }
}
