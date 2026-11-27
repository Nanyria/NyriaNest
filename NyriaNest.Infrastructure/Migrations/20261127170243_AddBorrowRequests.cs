using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NyriaNest.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBorrowRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReportedReturnedDate",
                schema: "library",
                table: "CheckOutItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReturnReminderSentAt",
                schema: "library",
                table: "CheckOutItems",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BorrowRequests",
                schema: "library",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BookId = table.Column<int>(type: "int", nullable: false),
                    BorrowerId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AnsweredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastReminderAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BorrowRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BorrowRequests_AspNetUsers_BorrowerId",
                        column: x => x.BorrowerId,
                        principalSchema: "identity",
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BorrowRequests_Books_BookId",
                        column: x => x.BookId,
                        principalSchema: "library",
                        principalTable: "Books",
                        principalColumn: "BookId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BorrowRequests_BookId_BorrowerId",
                schema: "library",
                table: "BorrowRequests",
                columns: new[] { "BookId", "BorrowerId" },
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_BorrowRequests_BorrowerId",
                schema: "library",
                table: "BorrowRequests",
                column: "BorrowerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BorrowRequests",
                schema: "library");

            migrationBuilder.DropColumn(
                name: "ReportedReturnedDate",
                schema: "library",
                table: "CheckOutItems");

            migrationBuilder.DropColumn(
                name: "ReturnReminderSentAt",
                schema: "library",
                table: "CheckOutItems");
        }
    }
}
