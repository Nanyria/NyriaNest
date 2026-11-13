using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NyriaNest.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class MoveTablesToSchemas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.EnsureSchema(
                name: "library");

            migrationBuilder.RenameTable(
                name: "StatusHistoryItems",
                newName: "StatusHistoryItems",
                newSchema: "library");

            migrationBuilder.RenameTable(
                name: "ReviewItems",
                newName: "ReviewItems",
                newSchema: "library");

            migrationBuilder.RenameTable(
                name: "ReservationItems",
                newName: "ReservationItems",
                newSchema: "library");

            migrationBuilder.RenameTable(
                name: "RatingItems",
                newName: "RatingItems",
                newSchema: "library");

            migrationBuilder.RenameTable(
                name: "NotificationItems",
                newName: "NotificationItems",
                newSchema: "library");

            migrationBuilder.RenameTable(
                name: "FavoriteItems",
                newName: "FavoriteItems",
                newSchema: "library");

            migrationBuilder.RenameTable(
                name: "CheckOutItems",
                newName: "CheckOutItems",
                newSchema: "library");

            migrationBuilder.RenameTable(
                name: "Books",
                newName: "Books",
                newSchema: "library");

            migrationBuilder.RenameTable(
                name: "AspNetUserTokens",
                newName: "AspNetUserTokens",
                newSchema: "identity");

            migrationBuilder.RenameTable(
                name: "AspNetUsers",
                newName: "AspNetUsers",
                newSchema: "identity");

            migrationBuilder.RenameTable(
                name: "AspNetUserRoles",
                newName: "AspNetUserRoles",
                newSchema: "identity");

            migrationBuilder.RenameTable(
                name: "AspNetUserLogins",
                newName: "AspNetUserLogins",
                newSchema: "identity");

            migrationBuilder.RenameTable(
                name: "AspNetUserClaims",
                newName: "AspNetUserClaims",
                newSchema: "identity");

            migrationBuilder.RenameTable(
                name: "AspNetRoles",
                newName: "AspNetRoles",
                newSchema: "identity");

            migrationBuilder.RenameTable(
                name: "AspNetRoleClaims",
                newName: "AspNetRoleClaims",
                newSchema: "identity");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "StatusHistoryItems",
                schema: "library",
                newName: "StatusHistoryItems");

            migrationBuilder.RenameTable(
                name: "ReviewItems",
                schema: "library",
                newName: "ReviewItems");

            migrationBuilder.RenameTable(
                name: "ReservationItems",
                schema: "library",
                newName: "ReservationItems");

            migrationBuilder.RenameTable(
                name: "RatingItems",
                schema: "library",
                newName: "RatingItems");

            migrationBuilder.RenameTable(
                name: "NotificationItems",
                schema: "library",
                newName: "NotificationItems");

            migrationBuilder.RenameTable(
                name: "FavoriteItems",
                schema: "library",
                newName: "FavoriteItems");

            migrationBuilder.RenameTable(
                name: "CheckOutItems",
                schema: "library",
                newName: "CheckOutItems");

            migrationBuilder.RenameTable(
                name: "Books",
                schema: "library",
                newName: "Books");

            migrationBuilder.RenameTable(
                name: "AspNetUserTokens",
                schema: "identity",
                newName: "AspNetUserTokens");

            migrationBuilder.RenameTable(
                name: "AspNetUsers",
                schema: "identity",
                newName: "AspNetUsers");

            migrationBuilder.RenameTable(
                name: "AspNetUserRoles",
                schema: "identity",
                newName: "AspNetUserRoles");

            migrationBuilder.RenameTable(
                name: "AspNetUserLogins",
                schema: "identity",
                newName: "AspNetUserLogins");

            migrationBuilder.RenameTable(
                name: "AspNetUserClaims",
                schema: "identity",
                newName: "AspNetUserClaims");

            migrationBuilder.RenameTable(
                name: "AspNetRoles",
                schema: "identity",
                newName: "AspNetRoles");

            migrationBuilder.RenameTable(
                name: "AspNetRoleClaims",
                schema: "identity",
                newName: "AspNetRoleClaims");
        }
    }
}
