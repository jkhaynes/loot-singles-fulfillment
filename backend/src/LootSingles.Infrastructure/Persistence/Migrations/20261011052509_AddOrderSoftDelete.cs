using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LootSingles.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Orders_ClaimedByEmployeeId", table: "Orders");

            migrationBuilder.DropIndex(name: "IX_Orders_TcgplayerOrderId", table: "Orders");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "DeletedByEmployeeId",
                table: "Orders",
                type: "int",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FirstClaimedAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true
            );

            // Mark existing orders that were already touched (021 R6), using the earliest time
            // available. A claim-and-release with nothing recorded can't be detected.
            migrationBuilder.Sql(
                """
                UPDATE o
                SET FirstClaimedAt = t.EarliestAt
                FROM Orders AS o
                CROSS APPLY (
                    SELECT MIN(v.At) AS EarliestAt
                    FROM (
                        SELECT o.ClaimedAt AS At
                        UNION ALL
                        SELECT MIN(ol.PickOutcomeRecordedAt)
                            FROM OrderLines AS ol WHERE ol.OrderId = o.Id
                        UNION ALL
                        SELECT MIN(pi.ReportedAt)
                            FROM PickingIssues AS pi
                            INNER JOIN OrderLines AS ol2 ON ol2.Id = pi.OrderLineId
                            WHERE ol2.OrderId = o.Id
                        UNION ALL
                        SELECT o.PackedAt
                    ) AS v
                ) AS t
                WHERE t.EarliestAt IS NOT NULL
                """
            );

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ClaimedByEmployeeId",
                table: "Orders",
                column: "ClaimedByEmployeeId",
                unique: true,
                filter: "[ClaimedByEmployeeId] IS NOT NULL AND [DeletedAt] IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Orders_DeletedByEmployeeId",
                table: "Orders",
                column: "DeletedByEmployeeId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TcgplayerOrderId",
                table: "Orders",
                column: "TcgplayerOrderId",
                unique: true,
                filter: "[DeletedAt] IS NULL"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Employees_DeletedByEmployeeId",
                table: "Orders",
                column: "DeletedByEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Employees_DeletedByEmployeeId",
                table: "Orders"
            );

            migrationBuilder.DropIndex(name: "IX_Orders_ClaimedByEmployeeId", table: "Orders");

            migrationBuilder.DropIndex(name: "IX_Orders_DeletedByEmployeeId", table: "Orders");

            migrationBuilder.DropIndex(name: "IX_Orders_TcgplayerOrderId", table: "Orders");

            migrationBuilder.DropColumn(name: "DeletedAt", table: "Orders");

            migrationBuilder.DropColumn(name: "DeletedByEmployeeId", table: "Orders");

            migrationBuilder.DropColumn(name: "FirstClaimedAt", table: "Orders");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ClaimedByEmployeeId",
                table: "Orders",
                column: "ClaimedByEmployeeId",
                unique: true,
                filter: "[ClaimedByEmployeeId] IS NOT NULL"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TcgplayerOrderId",
                table: "Orders",
                column: "TcgplayerOrderId",
                unique: true
            );
        }
    }
}
