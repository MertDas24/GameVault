using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameVault.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class RawgIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RawgId",
                table: "LibraryGames",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ReleaseDate",
                table: "LibraryGames",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LibraryGames_UserId_RawgId",
                table: "LibraryGames",
                columns: new[] { "UserId", "RawgId" },
                unique: true,
                filter: "[RawgId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LibraryGames_RawgId",
                table: "LibraryGames",
                sql: "[RawgId] IS NULL OR [RawgId] > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LibraryGames_UserId_RawgId",
                table: "LibraryGames");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LibraryGames_RawgId",
                table: "LibraryGames");

            migrationBuilder.DropColumn(
                name: "RawgId",
                table: "LibraryGames");

            migrationBuilder.DropColumn(
                name: "ReleaseDate",
                table: "LibraryGames");
        }
    }
}
