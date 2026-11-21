namespace NyriaNest.Application.Friends
{
    /// <summary>
    /// How many friends a user may share with, per sharing setting (#84, ADR 0005). Counts the
    /// user's own settings that are on, not friendships and not what friends share with them.
    /// Read from the "SharingLimits" section; lowering a limit turns nothing off, users above it
    /// just can't turn on more.
    /// </summary>
    public class SharingLimitsOptions
    {
        public const string SectionName = "SharingLimits";

        /// <summary>Friends who may see my physical library.</summary>
        public int PhysicalLibrary { get; set; } = 100;

        /// <summary>Friends who may read my e-books. Kept small: every one of them gets a readable copy.</summary>
        public int Ebooks { get; set; } = 10;
    }
}
