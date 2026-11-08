using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NyriaNest.Infrastructure.Migrations
{
    /// <summary>
    /// #111: the placeholder profile pictures (three SVG dragons) were replaced by
    /// pictures drawn for NyriaNest. Users who had picked a placeholder get the new
    /// picture closest to it, so their picture doesn't break and the API (which only
    /// accepts the current presets) doesn't refuse it on their next profile save.
    /// No schema change. (The model snapshot was also brought up to date with the
    /// entities' move to NyriaNest.Domain.Entities; that changes no tables.)
    /// </summary>
    public partial class ReplacePlaceholderProfilePictures : Migration
    {
        private const string Folder = "assets/images/profile-pictures/";

        // Old placeholder -> new picture
        private static readonly (string Old, string New)[] Pictures =
        [
            ("BlueKidDragon.svg", "BlueDragon.png"),
            ("RedDragon.svg", "RedDragon.png"),
            ("PinkKidDragon.svg", "GreenDragon.png"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (old, @new) in Pictures)
                Move(migrationBuilder, from: old, to: @new);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (old, @new) in Pictures)
                Move(migrationBuilder, from: @new, to: old);
        }

        private static void Move(MigrationBuilder migrationBuilder, string from, string to)
        {
            migrationBuilder.Sql($"""
                UPDATE [AspNetUsers]
                SET [ProfilePictureUrl] = N'{Folder}{to}'
                WHERE [ProfilePictureUrl] = N'{Folder}{from}';
                """);
        }
    }
}
