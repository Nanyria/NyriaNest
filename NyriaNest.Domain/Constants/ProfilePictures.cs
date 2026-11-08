namespace NyriaNest.Domain.Constants
{
    /// <summary>
    /// The pictures a user can choose without uploading their own. They're files
    /// in the frontend (src/assets), so the paths must match those.
    /// </summary>
    public static class ProfilePictures
    {
        public static readonly IReadOnlyList<string> Presets =
        [
            "assets/images/profile-pictures/BlueKidDragon.svg",
            "assets/images/profile-pictures/PinkKidDragon.svg",
            "assets/images/profile-pictures/RedDragon.svg",
        ];
    }
}
