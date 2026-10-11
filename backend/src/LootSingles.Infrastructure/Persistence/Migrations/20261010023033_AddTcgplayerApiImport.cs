using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LootSingles.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTcgplayerApiImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ImportSource",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0
            );

            migrationBuilder.AlterColumn<string>(
                name: "CollectorNumber",
                table: "OrderLines",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)"
            );

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "OrderLines",
                type: "nvarchar(2048)",
                maxLength: 2048,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "OrderLines",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true
            );

            migrationBuilder.AddColumn<int>(
                name: "Source",
                table: "ImportAttempts",
                type: "int",
                nullable: false,
                defaultValue: 0
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "ImportSource", table: "Orders");

            migrationBuilder.DropColumn(name: "ImageUrl", table: "OrderLines");

            migrationBuilder.DropColumn(name: "Language", table: "OrderLines");

            migrationBuilder.DropColumn(name: "Source", table: "ImportAttempts");

            migrationBuilder.AlterColumn<string>(
                name: "CollectorNumber",
                table: "OrderLines",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true
            );
        }
    }
}
