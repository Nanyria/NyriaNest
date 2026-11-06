using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NyriaNest.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEpubToBook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EpubFileKey",
                table: "Books",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1001,
                column: "EpubFileKey",
                value: null);

            migrationBuilder.UpdateData(
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1002,
                column: "EpubFileKey",
                value: null);

            migrationBuilder.UpdateData(
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1003,
                column: "EpubFileKey",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EpubFileKey",
                table: "Books");
        }
    }
}
