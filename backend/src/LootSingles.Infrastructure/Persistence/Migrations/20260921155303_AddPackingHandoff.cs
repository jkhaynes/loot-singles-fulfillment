using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LootSingles.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPackingHandoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PackedAt",
                table: "Orders",
                type: "datetimeoffset",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "PackedByEmployeeId",
                table: "Orders",
                type: "int",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "OrderPackingSlips",
                columns: table => new
                {
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    StoredAt = table.Column<DateTimeOffset>(
                        type: "datetimeoffset",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderPackingSlips", x => x.OrderId);
                    table.ForeignKey(
                        name: "FK_OrderPackingSlips_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "PackingSlipAccesses",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    EmployeeId = table.Column<int>(type: "int", nullable: false),
                    RetrievedAt = table.Column<DateTimeOffset>(
                        type: "datetimeoffset",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackingSlipAccesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackingSlipAccesses_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_PackingSlipAccesses_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Orders_PackedByEmployeeId",
                table: "Orders",
                column: "PackedByEmployeeId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PackingSlipAccesses_EmployeeId",
                table: "PackingSlipAccesses",
                column: "EmployeeId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PackingSlipAccesses_OrderId_RetrievedAt",
                table: "PackingSlipAccesses",
                columns: new[] { "OrderId", "RetrievedAt" }
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Employees_PackedByEmployeeId",
                table: "Orders",
                column: "PackedByEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Employees_PackedByEmployeeId",
                table: "Orders"
            );

            migrationBuilder.DropTable(name: "OrderPackingSlips");

            migrationBuilder.DropTable(name: "PackingSlipAccesses");

            migrationBuilder.DropIndex(name: "IX_Orders_PackedByEmployeeId", table: "Orders");

            migrationBuilder.DropColumn(name: "PackedAt", table: "Orders");

            migrationBuilder.DropColumn(name: "PackedByEmployeeId", table: "Orders");
        }
    }
}
