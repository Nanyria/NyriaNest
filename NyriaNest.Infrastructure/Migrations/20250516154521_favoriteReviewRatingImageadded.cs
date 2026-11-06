using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NyriaNest.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class favoriteReviewRatingImageadded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReservationItems_AspNetUsers_UserID",
                table: "ReservationItems");

            migrationBuilder.DropForeignKey(
                name: "FK_ReservationItems_Books_BookID",
                table: "ReservationItems");

            migrationBuilder.DropForeignKey(
                name: "FK_StatusHistoryItems_AspNetUsers_UserID",
                table: "StatusHistoryItems");

            migrationBuilder.DropForeignKey(
                name: "FK_StatusHistoryItems_Books_BookID",
                table: "StatusHistoryItems");

            migrationBuilder.RenameColumn(
                name: "UserID",
                table: "StatusHistoryItems",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "BookID",
                table: "StatusHistoryItems",
                newName: "BookId");

            migrationBuilder.RenameColumn(
                name: "StatusHistoryItemID",
                table: "StatusHistoryItems",
                newName: "StatusHistoryItemId");

            migrationBuilder.RenameIndex(
                name: "IX_StatusHistoryItems_UserID",
                table: "StatusHistoryItems",
                newName: "IX_StatusHistoryItems_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_StatusHistoryItems_BookID",
                table: "StatusHistoryItems",
                newName: "IX_StatusHistoryItems_BookId");

            migrationBuilder.RenameColumn(
                name: "UserID",
                table: "ReservationItems",
                newName: "UserId");

            migrationBuilder.RenameColumn(
                name: "BookID",
                table: "ReservationItems",
                newName: "BookId");

            migrationBuilder.RenameColumn(
                name: "ID",
                table: "ReservationItems",
                newName: "Id");

            migrationBuilder.RenameIndex(
                name: "IX_ReservationItems_UserID",
                table: "ReservationItems",
                newName: "IX_ReservationItems_UserId");

            migrationBuilder.RenameIndex(
                name: "IX_ReservationItems_BookID",
                table: "ReservationItems",
                newName: "IX_ReservationItems_BookId");

            migrationBuilder.RenameColumn(
                name: "BookID",
                table: "Books",
                newName: "BookId");

            migrationBuilder.AddColumn<string>(
                name: "CoverImagePath",
                table: "Books",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FavoriteItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1001, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FavoriteItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FavoriteItems_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FavoriteItems_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "BookId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ReviewItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1001, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    ReviewHeader = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReviewText = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewItems_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReviewItems_Books_BookId",
                        column: x => x.BookId,
                        principalTable: "Books",
                        principalColumn: "BookId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RatingItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1001, 1"),
                    ReviewItemId = table.Column<int>(type: "int", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RatingItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RatingItems_ReviewItems_ReviewItemId",
                        column: x => x.ReviewItemId,
                        principalTable: "ReviewItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.UpdateData(
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1001,
                column: "CoverImagePath",
                value: null);

            migrationBuilder.UpdateData(
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1002,
                column: "CoverImagePath",
                value: null);

            migrationBuilder.UpdateData(
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1003,
                column: "CoverImagePath",
                value: null);

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1001,
                column: "Timestamp",
                value: new DateTime(2025, 5, 15, 15, 45, 21, 418, DateTimeKind.Utc).AddTicks(2353));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1002,
                column: "Timestamp",
                value: new DateTime(2025, 5, 14, 15, 45, 21, 418, DateTimeKind.Utc).AddTicks(2358));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1003,
                column: "Timestamp",
                value: new DateTime(2025, 5, 13, 15, 45, 21, 418, DateTimeKind.Utc).AddTicks(2360));

            migrationBuilder.CreateIndex(
                name: "IX_FavoriteItems_BookId",
                table: "FavoriteItems",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_FavoriteItems_UserId",
                table: "FavoriteItems",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_RatingItems_ReviewItemId",
                table: "RatingItems",
                column: "ReviewItemId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewItems_BookId",
                table: "ReviewItems",
                column: "BookId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewItems_UserId",
                table: "ReviewItems",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ReservationItems_AspNetUsers_UserId",
                table: "ReservationItems",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ReservationItems_Books_BookId",
                table: "ReservationItems",
                column: "BookId",
                principalTable: "Books",
                principalColumn: "BookId",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StatusHistoryItems_AspNetUsers_UserId",
                table: "StatusHistoryItems",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StatusHistoryItems_Books_BookId",
                table: "StatusHistoryItems",
                column: "BookId",
                principalTable: "Books",
                principalColumn: "BookId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ReservationItems_AspNetUsers_UserId",
                table: "ReservationItems");

            migrationBuilder.DropForeignKey(
                name: "FK_ReservationItems_Books_BookId",
                table: "ReservationItems");

            migrationBuilder.DropForeignKey(
                name: "FK_StatusHistoryItems_AspNetUsers_UserId",
                table: "StatusHistoryItems");

            migrationBuilder.DropForeignKey(
                name: "FK_StatusHistoryItems_Books_BookId",
                table: "StatusHistoryItems");

            migrationBuilder.DropTable(
                name: "FavoriteItems");

            migrationBuilder.DropTable(
                name: "RatingItems");

            migrationBuilder.DropTable(
                name: "ReviewItems");

            migrationBuilder.DropColumn(
                name: "CoverImagePath",
                table: "Books");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "StatusHistoryItems",
                newName: "UserID");

            migrationBuilder.RenameColumn(
                name: "BookId",
                table: "StatusHistoryItems",
                newName: "BookID");

            migrationBuilder.RenameColumn(
                name: "StatusHistoryItemId",
                table: "StatusHistoryItems",
                newName: "StatusHistoryItemID");

            migrationBuilder.RenameIndex(
                name: "IX_StatusHistoryItems_UserId",
                table: "StatusHistoryItems",
                newName: "IX_StatusHistoryItems_UserID");

            migrationBuilder.RenameIndex(
                name: "IX_StatusHistoryItems_BookId",
                table: "StatusHistoryItems",
                newName: "IX_StatusHistoryItems_BookID");

            migrationBuilder.RenameColumn(
                name: "UserId",
                table: "ReservationItems",
                newName: "UserID");

            migrationBuilder.RenameColumn(
                name: "BookId",
                table: "ReservationItems",
                newName: "BookID");

            migrationBuilder.RenameColumn(
                name: "Id",
                table: "ReservationItems",
                newName: "ID");

            migrationBuilder.RenameIndex(
                name: "IX_ReservationItems_UserId",
                table: "ReservationItems",
                newName: "IX_ReservationItems_UserID");

            migrationBuilder.RenameIndex(
                name: "IX_ReservationItems_BookId",
                table: "ReservationItems",
                newName: "IX_ReservationItems_BookID");

            migrationBuilder.RenameColumn(
                name: "BookId",
                table: "Books",
                newName: "BookID");

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemID",
                keyValue: 1001,
                column: "Timestamp",
                value: new DateTime(2025, 5, 14, 13, 8, 23, 798, DateTimeKind.Utc).AddTicks(6446));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemID",
                keyValue: 1002,
                column: "Timestamp",
                value: new DateTime(2025, 5, 13, 13, 8, 23, 798, DateTimeKind.Utc).AddTicks(6451));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemID",
                keyValue: 1003,
                column: "Timestamp",
                value: new DateTime(2025, 5, 12, 13, 8, 23, 798, DateTimeKind.Utc).AddTicks(6452));

            migrationBuilder.AddForeignKey(
                name: "FK_ReservationItems_AspNetUsers_UserID",
                table: "ReservationItems",
                column: "UserID",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ReservationItems_Books_BookID",
                table: "ReservationItems",
                column: "BookID",
                principalTable: "Books",
                principalColumn: "BookID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StatusHistoryItems_AspNetUsers_UserID",
                table: "StatusHistoryItems",
                column: "UserID",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StatusHistoryItems_Books_BookID",
                table: "StatusHistoryItems",
                column: "BookID",
                principalTable: "Books",
                principalColumn: "BookID",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
