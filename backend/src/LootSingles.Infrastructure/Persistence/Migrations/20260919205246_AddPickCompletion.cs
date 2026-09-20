using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LootSingles.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPickCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentPickingIssueId",
                table: "OrderLines",
                type: "int",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "PickOutcome",
                table: "OrderLines",
                type: "int",
                nullable: true
            );

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PickOutcomeRecordedAt",
                table: "OrderLines",
                type: "datetimeoffset",
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "PickOutcomeRecordedByEmployeeId",
                table: "OrderLines",
                type: "int",
                nullable: true
            );

            migrationBuilder.CreateTable(
                name: "PickingIssues",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderLineId = table.Column<int>(type: "int", nullable: false),
                    IssueType = table.Column<string>(
                        type: "nvarchar(50)",
                        maxLength: 50,
                        nullable: false
                    ),
                    RequiredQuantity = table.Column<int>(type: "int", nullable: true),
                    FoundQuantity = table.Column<int>(type: "int", nullable: true),
                    Note = table.Column<string>(
                        type: "nvarchar(500)",
                        maxLength: 500,
                        nullable: true
                    ),
                    ReportedByEmployeeId = table.Column<int>(type: "int", nullable: false),
                    ReportedAt = table.Column<DateTimeOffset>(
                        type: "datetimeoffset",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PickingIssues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PickingIssues_Employees_ReportedByEmployeeId",
                        column: x => x.ReportedByEmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_PickingIssues_OrderLines_OrderLineId",
                        column: x => x.OrderLineId,
                        principalTable: "OrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_CurrentPickingIssueId",
                table: "OrderLines",
                column: "CurrentPickingIssueId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_PickOutcomeRecordedByEmployeeId",
                table: "OrderLines",
                column: "PickOutcomeRecordedByEmployeeId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PickingIssues_OrderLineId",
                table: "PickingIssues",
                column: "OrderLineId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_PickingIssues_ReportedByEmployeeId",
                table: "PickingIssues",
                column: "ReportedByEmployeeId"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLines_Employees_PickOutcomeRecordedByEmployeeId",
                table: "OrderLines",
                column: "PickOutcomeRecordedByEmployeeId",
                principalTable: "Employees",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict
            );

            migrationBuilder.AddForeignKey(
                name: "FK_OrderLines_PickingIssues_CurrentPickingIssueId",
                table: "OrderLines",
                column: "CurrentPickingIssueId",
                principalTable: "PickingIssues",
                principalColumn: "Id"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OrderLines_Employees_PickOutcomeRecordedByEmployeeId",
                table: "OrderLines"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_OrderLines_PickingIssues_CurrentPickingIssueId",
                table: "OrderLines"
            );

            migrationBuilder.DropTable(name: "PickingIssues");

            migrationBuilder.DropIndex(
                name: "IX_OrderLines_CurrentPickingIssueId",
                table: "OrderLines"
            );

            migrationBuilder.DropIndex(
                name: "IX_OrderLines_PickOutcomeRecordedByEmployeeId",
                table: "OrderLines"
            );

            migrationBuilder.DropColumn(name: "CurrentPickingIssueId", table: "OrderLines");

            migrationBuilder.DropColumn(name: "PickOutcome", table: "OrderLines");

            migrationBuilder.DropColumn(name: "PickOutcomeRecordedAt", table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "PickOutcomeRecordedByEmployeeId",
                table: "OrderLines"
            );
        }
    }
}
