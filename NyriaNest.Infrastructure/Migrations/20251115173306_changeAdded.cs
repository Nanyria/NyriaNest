using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NyriaNest.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class changeAdded : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1001,
                column: "Language",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1002,
                column: "Language",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1003,
                column: "Language",
                value: 1);

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1001,
                columns: new[] { "BookStatus", "Notes", "Timestamp" },
                values: new object[] { 5, "Initial added status", new DateTime(2025, 11, 14, 17, 33, 5, 616, DateTimeKind.Utc).AddTicks(6933) });

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1002,
                columns: new[] { "BookStatus", "Notes", "Timestamp" },
                values: new object[] { 5, "Initial added status", new DateTime(2025, 11, 13, 17, 33, 5, 616, DateTimeKind.Utc).AddTicks(6937) });

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1003,
                columns: new[] { "BookStatus", "Notes", "Timestamp" },
                values: new object[] { 5, "Initial added status", new DateTime(2025, 11, 12, 17, 33, 5, 616, DateTimeKind.Utc).AddTicks(6938) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1001,
                column: "Language",
                value: 0);

            migrationBuilder.UpdateData(
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1002,
                column: "Language",
                value: 0);

            migrationBuilder.UpdateData(
                table: "Books",
                keyColumn: "BookId",
                keyValue: 1003,
                column: "Language",
                value: 0);

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1001,
                columns: new[] { "BookStatus", "Notes", "Timestamp" },
                values: new object[] { 0, "Initial status", new DateTime(2025, 11, 14, 15, 4, 59, 869, DateTimeKind.Utc).AddTicks(3990) });

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1002,
                columns: new[] { "BookStatus", "Notes", "Timestamp" },
                values: new object[] { 2, "Initial status", new DateTime(2025, 11, 13, 15, 4, 59, 869, DateTimeKind.Utc).AddTicks(3994) });

            migrationBuilder.UpdateData(
                table: "StatusHistoryItems",
                keyColumn: "StatusHistoryItemId",
                keyValue: 1003,
                columns: new[] { "BookStatus", "Notes", "Timestamp" },
                values: new object[] { 1, "Initial status", new DateTime(2025, 11, 12, 15, 4, 59, 869, DateTimeKind.Utc).AddTicks(3995) });
        }
    }
}
