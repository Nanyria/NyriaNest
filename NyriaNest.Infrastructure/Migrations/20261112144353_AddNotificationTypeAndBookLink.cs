using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NyriaNest.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationTypeAndBookLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BookId",
                table: "NotificationItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "NotificationItems",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_NotificationItems_BookId",
                table: "NotificationItems",
                column: "BookId");

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationItems_Books_BookId",
                table: "NotificationItems",
                column: "BookId",
                principalTable: "Books",
                principalColumn: "BookId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NotificationItems_Books_BookId",
                table: "NotificationItems");

            migrationBuilder.DropIndex(
                name: "IX_NotificationItems_BookId",
                table: "NotificationItems");

            migrationBuilder.DropColumn(
                name: "BookId",
                table: "NotificationItems");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "NotificationItems");
        }
    }
}
