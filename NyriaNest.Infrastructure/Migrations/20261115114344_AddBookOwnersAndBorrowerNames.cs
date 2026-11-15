using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace NyriaNest.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookOwnersAndBorrowerNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // #83: the shared library holds e-books only (ADR 0004), and physical books now need
            // an owner, so today's physical books (Paperback = 1, Hardcover = 2) are deleted.
            // Loans and reservations are Restrict, so they go first; history, reviews (and their
            // ratings) and favourites go by cascade; notifications keep their text and lose the
            // book link (SetNull). Nobody is notified. Runs before the DeleteData calls below,
            // which then find their rows already gone.
            migrationBuilder.Sql(@"
DELETE c FROM library.CheckOutItems c
    JOIN library.Books b ON b.BookId = c.BookId
    WHERE b.BookType IN (1, 2);
DELETE r FROM library.ReservationItems r
    JOIN library.Books b ON b.BookId = r.BookId
    WHERE b.BookType IN (1, 2);
DELETE FROM library.Books WHERE BookType IN (1, 2);");

            migrationBuilder.DeleteData(
                schema: "library",
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1001);

            migrationBuilder.DeleteData(
                schema: "library",
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1002);

            migrationBuilder.DeleteData(
                schema: "library",
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1003);

            migrationBuilder.DeleteData(
                schema: "library",
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1001);

            migrationBuilder.DeleteData(
                schema: "library",
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1002);

            migrationBuilder.DeleteData(
                schema: "library",
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1003);

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                schema: "library",
                table: "CheckOutItems",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddColumn<string>(
                name: "BorrowerName",
                schema: "library",
                table: "CheckOutItems",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                schema: "library",
                table: "Books",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.UpdateData(
                schema: "library",
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1004,
                column: "OwnerId",
                value: null);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CheckOutItems_UserOrName",
                schema: "library",
                table: "CheckOutItems",
                sql: "([UserId] IS NOT NULL AND [BorrowerName] IS NULL) OR ([UserId] IS NULL AND [BorrowerName] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_Books_OwnerId",
                schema: "library",
                table: "Books",
                column: "OwnerId");

            migrationBuilder.AddForeignKey(
                name: "FK_Books_AspNetUsers_OwnerId",
                schema: "library",
                table: "Books",
                column: "OwnerId",
                principalSchema: "identity",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Books_AspNetUsers_OwnerId",
                schema: "library",
                table: "Books");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CheckOutItems_UserOrName",
                schema: "library",
                table: "CheckOutItems");

            migrationBuilder.DropIndex(
                name: "IX_Books_OwnerId",
                schema: "library",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "BorrowerName",
                schema: "library",
                table: "CheckOutItems");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                schema: "library",
                table: "Books");

            // UserId becomes required again, so loans lent to a name can't stay. The physical
            // books deleted in Up aren't restored (except the three seeded ones below): use the
            // backup taken before this migration for that.
            migrationBuilder.Sql("DELETE FROM library.CheckOutItems WHERE UserId IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "UserId",
                schema: "library",
                table: "CheckOutItems",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.InsertData(
                schema: "library",
                table: "Books",
                columns: new[] { "BookId", "AdminStatus", "AdminStatusNotes", "AdminStatusSetAt", "Author", "AvailabilityDate", "BookDescription", "BookStatus", "BookType", "CoverImagePath", "DateAdded", "EpubFileKey", "EpubFileName", "EpubFileSize", "Genre", "Language", "PublicationYear", "Title" },
                values: new object[,]
                {
                    { 1001, null, null, null, "F. Scott Fitzgerald", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Lorem Ipsum", 0, 2, null, new DateTime(2025, 5, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 9, 1, 1925, "The Great Gatsby" },
                    { 1002, null, null, null, "Harper Lee", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Lorem Ipsum", 0, 2, null, new DateTime(2025, 5, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 9, 1, 1960, "To Kill a Mockingbird" },
                    { 1003, null, null, null, "George Orwell", new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Lorem Ipsum", 0, 1, null, new DateTime(2025, 5, 15, 0, 0, 0, 0, DateTimeKind.Utc), null, null, null, 9, 1, 1949, "1984" }
                });

            migrationBuilder.InsertData(
                schema: "library",
                table: "StatusHistoryItems",
                columns: new[] { "StatusHistoryItemId", "BookId", "BookStatus", "Notes", "Timestamp", "UserId" },
                values: new object[,]
                {
                    { 1001, 1001, 5, "Initial added status", new DateTime(2025, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { 1002, 1002, 5, "Initial added status", new DateTime(2025, 5, 13, 0, 0, 0, 0, DateTimeKind.Utc), null },
                    { 1003, 1003, 5, "Initial added status", new DateTime(2025, 5, 12, 0, 0, 0, 0, DateTimeKind.Utc), null }
                });
        }
    }
}
