using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NyriaNest.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddBookAdminStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AdminStatus",
                table: "Books",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AdminStatusNotes",
                table: "Books",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AdminStatusSetAt",
                table: "Books",
                type: "datetime2",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1001,
                columns: new[] { "AdminStatus", "AdminStatusNotes", "AdminStatusSetAt" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1002,
                columns: new[] { "AdminStatus", "AdminStatusNotes", "AdminStatusSetAt" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1003,
                columns: new[] { "AdminStatus", "AdminStatusNotes", "AdminStatusSetAt" },
                values: new object[] { null, null, null });

            migrationBuilder.UpdateData(
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1004,
                columns: new[] { "AdminStatus", "AdminStatusNotes", "AdminStatusSetAt" },
                values: new object[] { null, null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdminStatus",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "AdminStatusNotes",
                table: "Books");

            migrationBuilder.DropColumn(
                name: "AdminStatusSetAt",
                table: "Books");
        }
    }
}
