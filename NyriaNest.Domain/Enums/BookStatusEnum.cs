using System.ComponentModel;

namespace NyriaNest.Domain.Enums
{
    public enum BookStatusEnum
    {

        [Description("Available")]
        Available,
        [Description("Reserved")]
        Reserved,
        [Description("Checked Out")]
        CheckedOut,
        [Description("Returned")]
        Returned,
        [Description("Overdue")]
        Overdue,
        [Description("Added")]
        Added,
        [Description("Lost")]
        Lost,
        [Description("Removed")]
        Removed,

        // E-book states for the admin catalogue (#83). Like Overdue, they're worked
        // out (from the EPUB file) and never stored on a book.
        [Description("Ready")]
        Ready,
        [Description("No file")]
        NoFile
    }
}
