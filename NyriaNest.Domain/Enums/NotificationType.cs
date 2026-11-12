namespace NyriaNest.Domain.Enums
{
    /// <summary>
    /// What a notification is about, so the UI can pick an icon and a link.
    /// Saved as a number: never change or reuse a number, only add new types at the end.
    /// Only BookRemoved is sent today; the rest are reserved for the issues named on each.
    /// </summary>
    public enum NotificationType
    {
        /// <summary>A book the user was involved with was removed (#75).</summary>
        BookRemoved = 0,

        /// <summary>Someone asked to borrow the user's book (#85/#86).</summary>
        LoanRequested = 1,

        /// <summary>The owner approved or declined the user's loan request (#85/#86).</summary>
        LoanRequestAnswered = 2,

        /// <summary>A borrowed book is due back soon (#78).</summary>
        ReturnReminderDue = 3,

        /// <summary>A borrowed book is past its due date (#78).</summary>
        LoanOverdue = 4,

        /// <summary>The borrower marked the user's book as returned; the owner confirms (#85/#86).</summary>
        ReturnMarked = 5,

        /// <summary>The owner confirmed the book is back (#85/#86).</summary>
        ReturnConfirmed = 6,

        /// <summary>A book the user reserved or queued for is now free.</summary>
        ReservationAvailable = 7,

        /// <summary>An admin approved or declined the user's shared library offer (#88/#89).</summary>
        SharedLibraryRequestAnswered = 8,

        /// <summary>Someone sent the user a friend request (#82/#84).</summary>
        FriendRequestReceived = 9,

        /// <summary>Someone accepted the user's friend request (#82/#84).</summary>
        FriendRequestAccepted = 10,

        /// <summary>A friend added a new book; will be possible to turn off (#82/#84).</summary>
        FriendAddedBook = 11,
    }
}
