using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NyriaNest.Api.Migrations
{
    /// <summary>
    /// Renames the Swedish role names to English: Administratör → Admin, Användare → User.
    /// Renames the existing role rows (so users keep their role) and the text in
    /// AspNetUsers.Role. No schema change. Logged-in admins get the new name in their
    /// token the next time they log in.
    /// </summary>
    public partial class RenameRolesToEnglish : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            Rename(migrationBuilder, from: "Administratör", to: "Admin");
            Rename(migrationBuilder, from: "Användare", to: "User");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            Rename(migrationBuilder, from: "Admin", to: "Administratör");
            Rename(migrationBuilder, from: "User", to: "Användare");
        }

        private static void Rename(MigrationBuilder migrationBuilder, string from, string to)
        {
            // N'...' so SQL Server keeps å/ä/ö. NormalizedName is what Identity looks roles up by.
            migrationBuilder.Sql($"""
                UPDATE [AspNetRoles]
                SET [Name] = N'{to}', [NormalizedName] = N'{to.ToUpperInvariant()}'
                WHERE [Name] = N'{from}';

                UPDATE [AspNetUsers]
                SET [Role] = N'{to}'
                WHERE [Role] = N'{from}';
                """);
        }
    }
}
