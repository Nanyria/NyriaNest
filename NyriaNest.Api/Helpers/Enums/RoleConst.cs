namespace NyriaNest.Api.Helpers.Enums
{
    // The role names, used everywhere: [Authorize], seeding and the database.
    // They were Swedish ("Administratör", "Användare") until the
    // RenameRolesToEnglish migration renamed the existing rows.
    public static class Roles
    {
        public const string SuperAdmin = "SuperAdmin";
        public const string Admin = "Admin";
        public const string User = "User";
        public static readonly List<string> AllRoles = new() { SuperAdmin, Admin, User};
    }
}
