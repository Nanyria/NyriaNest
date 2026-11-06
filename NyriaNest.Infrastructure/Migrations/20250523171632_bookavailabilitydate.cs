using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NyriaNest.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class bookavailabilitydate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AvailabilityDate",
                table: "Books",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.UpdateData(
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1001,
                column: "AvailabilityDate",
                value: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.UpdateData(
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1002,
                column: "AvailabilityDate",
                value: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.UpdateData(
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1003,
                column: "AvailabilityDate",
                value: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1001,
                column: "Timestamp",
                value: new DateTime(2025, 5, 22, 17, 16, 31, 647, DateTimeKind.Utc).AddTicks(5430));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1002,
                column: "Timestamp",
                value: new DateTime(2025, 5, 21, 17, 16, 31, 647, DateTimeKind.Utc).AddTicks(5435));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1003,
                column: "Timestamp",
                value: new DateTime(2025, 5, 20, 17, 16, 31, 647, DateTimeKind.Utc).AddTicks(5436));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvailabilityDate",
                table: "Books");

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1001,
                column: "Timestamp",
                value: new DateTime(2025, 5, 20, 15, 43, 46, 819, DateTimeKind.Utc).AddTicks(6610));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1002,
                column: "Timestamp",
                value: new DateTime(2025, 5, 19, 15, 43, 46, 819, DateTimeKind.Utc).AddTicks(6615));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1003,
                column: "Timestamp",
                value: new DateTime(2025, 5, 18, 15, 43, 46, 819, DateTimeKind.Utc).AddTicks(6616));
        }
    }
}
