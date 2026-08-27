using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LootSingles.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderClaiming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClaimedAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "ClaimedByEmployeeId",
                table: "Orders",
                type: "int",
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ClaimedByEmployeeId",
                table: "Orders",
                column: "ClaimedByEmployeeId",
                unique: true,
                filter: "[ClaimedByEmployeeId] IS NOT NULL"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Employees_ClaimedByEmployeeId",
                table: "Orders",
                column: "ClaimedByEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Employees_ClaimedByEmployeeId",
                table: "Orders"
            );

            migrationBuilder.DropIndex(name: "IX_Orders_ClaimedByEmployeeId", table: "Orders");

            migrationBuilder.DropColumn(name: "ClaimedAt", table: "Orders");

            migrationBuilder.DropColumn(name: "ClaimedByEmployeeId", table: "Orders");
        }
    }
}
