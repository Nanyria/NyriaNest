namespace NyriaNest.Domain.Enums
{
    /// <summary>
    /// What a user can share with a friend, one setting per friend and direction (#84, ADR 0005).
    /// Each has its own limit: 100 friends for the physical library, 10 for e-books.
    /// </summary>
    public enum SharingSetting
    {
        /// <summary>"Share my physical library": the friend sees the list and can ask to borrow (#85).</summary>
        PhysicalLibrary = 0,

        /// <summary>"Share my e-books": the friend can read them in the app (#87).</summary>
        Ebooks = 1,
    }
}
