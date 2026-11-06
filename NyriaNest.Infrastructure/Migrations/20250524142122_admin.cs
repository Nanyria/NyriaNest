using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NyriaNest.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class admin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1001,
                column: "Timestamp",
                value: new DateTime(2025, 5, 23, 14, 21, 22, 244, DateTimeKind.Utc).AddTicks(6897));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1002,
                column: "Timestamp",
                value: new DateTime(2025, 5, 22, 14, 21, 22, 244, DateTimeKind.Utc).AddTicks(6902));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1003,
                column: "Timestamp",
                value: new DateTime(2025, 5, 21, 14, 21, 22, 244, DateTimeKind.Utc).AddTicks(6903));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
    }
}
