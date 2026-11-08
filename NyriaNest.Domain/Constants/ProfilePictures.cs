namespace NyriaNest.Domain.Constants
{
    /// <summary>
    /// The pictures a user can choose without uploading their own. They're files
    /// in the frontend (src/assets), so the paths must match those, and the
    /// frontend's picker and default picture.
    /// The placeholder SVGs these replaced (#111) were moved to these by the
    /// ReplacePlaceholderProfilePictures migration.
    /// </summary>
    public static class ProfilePictures
    {
        public const string Red = "assets/images/profile-pictures/RedDragon.png";
        public const string Blue = "assets/images/profile-pictures/BlueDragon.png";
        public const string Green = "assets/images/profile-pictures/GreenDragon.png";

        public static readonly IReadOnlyList<string> Presets = [Red, Blue, Green];
    }
}
