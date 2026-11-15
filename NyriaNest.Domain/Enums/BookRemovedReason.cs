namespace NyriaNest.Domain.Enums
{
    /// <summary>
    /// Why a book went away, which decides who is told and what the notification says.
    /// More reasons come with #83, #85 and #90.
    /// </summary>
    public enum BookRemovedReason
    {
        /// <summary>An admin deleted the book; its reservations were cancelled.</summary>
        DeletedByAdmin,

        /// <summary>The owner deleted their own book. Nobody to tell until friends can see it (#85).</summary>
        DeletedByOwner,

        /// <summary>An admin deleted someone's own book; the owner is told, with the admin's reason.</summary>
        DeletedByAdminFromOwner,
    }
}
