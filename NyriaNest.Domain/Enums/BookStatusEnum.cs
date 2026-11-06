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
        Removed
    }
}
