using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NyriaNest.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class adinuserseed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemID",
                keyValue: 1001,
                column: "Timestamp",
                value: new DateTime(2025, 5, 14, 8, 51, 17, 597, DateTimeKind.Utc).AddTicks(2471));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemID",
                keyValue: 1002,
                column: "Timestamp",
                value: new DateTime(2025, 5, 13, 8, 51, 17, 597, DateTimeKind.Utc).AddTicks(2476));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemID",
                keyValue: 1003,
                column: "Timestamp",
                value: new DateTime(2025, 5, 12, 8, 51, 17, 597, DateTimeKind.Utc).AddTicks(2478));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemID",
                keyValue: 1001,
                column: "Timestamp",
                value: new DateTime(2025, 5, 14, 8, 37, 51, 320, DateTimeKind.Utc).AddTicks(8788));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemID",
                keyValue: 1002,
                column: "Timestamp",
                value: new DateTime(2025, 5, 13, 8, 37, 51, 320, DateTimeKind.Utc).AddTicks(8795));

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemID",
                keyValue: 1003,
                column: "Timestamp",
                value: new DateTime(2025, 5, 12, 8, 37, 51, 320, DateTimeKind.Utc).AddTicks(8796));
        }
    }
}
