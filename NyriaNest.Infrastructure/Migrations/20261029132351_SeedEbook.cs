using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NyriaNest.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedEbook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Books",
                columns: new[] { "BookId", "Author", "AvailabilityDate", "BookDescription", "BookStatus", "BookType", "CoverImagePath", "DateAdded", "EpubFileKey", "Genre", "Language", "PublicationYear", "Title" },
                values: new object[] { 1004, "Diana Wynne Jones", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Sophie is turned into an old woman by a witch's curse and seeks help from the wizard Howl in his moving castle.", 0, 3, null, new DateTime(2025, 5, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, 1, 1, 1986, "Howl's Moving Castle" });

            migrationBuilder.InsertData(
                table: "StatusHistoryItems",
                columns: new[] { "StatusHistoryItemId", "BookId", "BookStatus", "Notes", "Timestamp", "UserId" },
                values: new object[] { 1004, 1004, 5, "Initial added status", new DateTime(2025, 5, 11, 0, 0, 0, 0, DateTimeKind.Utc), null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1004);

            migrationBuilder.DeleteData(
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1004);
        }
    }
}
