using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LootSingles.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmployeeAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmployeeAuditEvents",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorEmployeeId = table.Column<int>(type: "int", nullable: false),
                    TargetEmployeeId = table.Column<int>(type: "int", nullable: true),
                    ActionType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(
                        type: "datetimeoffset",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmployeeAuditEvents", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "Employees",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    NormalizedUsername = table.Column<string>(
                        type: "nvarchar(450)",
                        nullable: false
                    ),
                    DisplayName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PinHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    FailedAttemptCount = table.Column<int>(type: "int", nullable: false),
                    IsLocked = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(
                        type: "datetimeoffset",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => x.Id);
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAuditEvents_ActorEmployeeId",
                table: "EmployeeAuditEvents",
                column: "ActorEmployeeId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_EmployeeAuditEvents_TargetEmployeeId",
                table: "EmployeeAuditEvents",
                column: "TargetEmployeeId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Employees_NormalizedUsername",
                table: "Employees",
                column: "NormalizedUsername",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "EmployeeAuditEvents");

            migrationBuilder.DropTable(name: "Employees");
        }
    }
}
