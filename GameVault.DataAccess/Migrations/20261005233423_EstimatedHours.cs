using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameVault.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class EstimatedHours : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EstimatedHours",
                table: "LibraryGames",
                type: "int",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_LibraryGames_EstimatedHours",
                table: "LibraryGames",
                sql: "[EstimatedHours] IS NULL OR ([EstimatedHours] >= 1 AND [EstimatedHours] <= 1000)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LibraryGames_EstimatedHours",
                table: "LibraryGames");

            migrationBuilder.DropColumn(
                name: "EstimatedHours",
                table: "LibraryGames");
        }
    }
}
