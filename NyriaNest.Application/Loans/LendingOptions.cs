namespace NyriaNest.Application.Loans
{
    /// <summary>
    /// The timings for lending to friends (#85), read from the "Lending" section; the class
    /// holds the defaults. Used by the daily job that reminds owners and expires requests.
    /// </summary>
    public class LendingOptions
    {
        public const string SectionName = "Lending";

        /// <summary>
        /// Remind the owner this many days after a request they haven't answered, and again
        /// every as many days about a return they haven't confirmed.
        /// </summary>
        public int RequestReminderDays { get; set; } = 14;

        /// <summary>A request the owner hasn't answered expires after this many months; the friend may ask again.</summary>
        public int RequestExpiryMonths { get; set; } = 1;
    }
}
