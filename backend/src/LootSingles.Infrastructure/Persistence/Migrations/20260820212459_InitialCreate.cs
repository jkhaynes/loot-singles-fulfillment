using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LootSingles.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ImportAttempts",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StartedAt = table.Column<DateTimeOffset>(
                        type: "datetimeoffset",
                        nullable: false
                    ),
                    CompletedAt = table.Column<DateTimeOffset>(
                        type: "datetimeoffset",
                        nullable: true
                    ),
                    AttemptFailureCode = table.Column<int>(type: "int", nullable: true),
                    AttemptFailureMessage = table.Column<string>(
                        type: "nvarchar(max)",
                        nullable: true
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportAttempts", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TcgplayerOrderId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(
                        type: "datetimeoffset",
                        nullable: false
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                }
            );

            migrationBuilder.CreateTable(
                name: "ImportOrderResults",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ImportAttemptId = table.Column<int>(type: "int", nullable: false),
                    SourceOrderIdentifier = table.Column<string>(
                        type: "nvarchar(max)",
                        nullable: true
                    ),
                    Outcome = table.Column<int>(type: "int", nullable: false),
                    FailureCode = table.Column<int>(type: "int", nullable: true),
                    FailureMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ResultingOrderId = table.Column<int>(type: "int", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportOrderResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImportOrderResults_ImportAttempts_ImportAttemptId",
                        column: x => x.ImportAttemptId,
                        principalTable: "ImportAttempts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                    table.ForeignKey(
                        name: "FK_ImportOrderResults_Orders_ResultingOrderId",
                        column: x => x.ResultingOrderId,
                        principalTable: "Orders",
                        principalColumn: "Id"
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "OrderLines",
                columns: table => new
                {
                    Id = table
                        .Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    RawDescription = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProductLine = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Set = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CollectorNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Rarity = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Condition = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Variant = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderLines_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "IX_ImportOrderResults_ImportAttemptId",
                table: "ImportOrderResults",
                column: "ImportAttemptId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_ImportOrderResults_ResultingOrderId",
                table: "ImportOrderResults",
                column: "ResultingOrderId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_OrderLines_OrderId",
                table: "OrderLines",
                column: "OrderId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Orders_TcgplayerOrderId",
                table: "Orders",
                column: "TcgplayerOrderId",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "ImportOrderResults");

            migrationBuilder.DropTable(name: "OrderLines");

            migrationBuilder.DropTable(name: "ImportAttempts");

            migrationBuilder.DropTable(name: "Orders");
        }
    }
}
