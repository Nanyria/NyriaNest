namespace NyriaNest.Domain.Enums
{
    /// <summary>
    /// Why a book went away, which decides what the notification says. Everyone with a
    /// current interaction (a reservation or a favorite) is told whatever the reason (#83).
    /// </summary>
    public enum BookRemovedReason
    {
        /// <summary>An admin deleted a shared-library book.</summary>
        RemovedFromSharedLibrary,

        /// <summary>The owner deleted their own book.</summary>
        DeletedByOwner,

        /// <summary>An admin deleted someone's own book; the owner is also told, with the admin's reason.</summary>
        DeletedByAdminFromOwner,
    }
}
