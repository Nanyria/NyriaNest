using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NyriaNest.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class admintest2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1001,
                column: "Timestamp",
                value: new DateTime(2025, 5, 23, 14, 24, 21, 725, DateTimeKind.Utc).AddTicks(3957));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1002,
                column: "Timestamp",
                value: new DateTime(2025, 5, 22, 14, 24, 21, 725, DateTimeKind.Utc).AddTicks(3962));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1003,
                column: "Timestamp",
                value: new DateTime(2025, 5, 21, 14, 24, 21, 725, DateTimeKind.Utc).AddTicks(3963));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
    }
}
