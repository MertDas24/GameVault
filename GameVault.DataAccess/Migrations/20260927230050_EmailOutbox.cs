using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameVault.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class EmailOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Recipient = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ProtectedBody = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    DedupKey = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxEmails", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEmails_CompletedAtUtc",
                table: "OutboxEmails",
                column: "CompletedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEmails_DedupKey",
                table: "OutboxEmails",
                column: "DedupKey",
                unique: true,
                filter: "[DedupKey] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxEmails_Status_NextAttemptAtUtc",
                table: "OutboxEmails",
                columns: new[] { "Status", "NextAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutboxEmails");
        }
    }
}
