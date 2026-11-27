using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NyriaNest.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBorrowRequestAvailableAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AvailableAt",
                schema: "library",
                table: "BorrowRequests",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            // Requests made before this column: the book was free when they asked
            migrationBuilder.Sql("UPDATE [library].[BorrowRequests] SET [AvailableAt] = [RequestedAt];");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvailableAt",
                schema: "library",
                table: "BorrowRequests");
        }
    }
}
