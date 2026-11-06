using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NyriaNest.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FixSeedTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1001,
                column: "Timestamp",
                value: new DateTime(2025, 5, 14, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1002,
                column: "Timestamp",
                value: new DateTime(2025, 5, 13, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1003,
                column: "Timestamp",
                value: new DateTime(2025, 5, 12, 0, 0, 0, 0, DateTimeKind.Utc));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1001,
                column: "Timestamp",
                value: new DateTime(2025, 11, 14, 17, 33, 5, 616, DateTimeKind.Utc).AddTicks(6933));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1002,
                column: "Timestamp",
                value: new DateTime(2025, 11, 13, 17, 33, 5, 616, DateTimeKind.Utc).AddTicks(6937));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1003,
                column: "Timestamp",
                value: new DateTime(2025, 11, 12, 17, 33, 5, 616, DateTimeKind.Utc).AddTicks(6938));
        }
    }
}
