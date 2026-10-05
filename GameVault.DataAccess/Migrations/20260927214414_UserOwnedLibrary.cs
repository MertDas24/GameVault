using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GameVault.DataAccess.Migrations
{
    /// <inheritdoc />
    public partial class UserOwnedLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Veri koruyan dönüşüm: eski global katalog (Games + UserGames + GameGenres),
            // kullanıcıya ait LibraryGames kayıtlarına kopyalanır; eski tablolar ancak
            // kopyalama bittikten sonra kaldırılır. EF Core migration'ı tek transaction'da çalıştırır.
            // Kullanıcı kütüphanesine dönüştürülemeyen veriler (hiçbir kütüphanede olmayan katalog oyunları,
            // katalogdaki çıkış tarihleri, sahibi silinmiş UserGames kayıtları) SİLİNMEZ: eski tabloların tam
            // kopyası Legacy_Games / Legacy_GameGenres / Legacy_UserGames arşiv tablolarında kalır.
            // İnceleyip gerekmediğine karar verdikten sonra elle kaldırılabilir (bkz. docs/OPERATIONS.md).

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "AspNetUsers",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "AspNetUsers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.Sql("UPDATE [AspNetUsers] SET [CreatedAtUtc] = SYSUTCDATETIME() WHERE [CreatedAtUtc] = '0001-01-01';");

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FriendlyName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Xml = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LibraryGames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CoverImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PlatformId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: true),
                    PlaytimeHours = table.Column<int>(type: "int", nullable: false),
                    IsFavorite = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CompletionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DateAdded = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryGames", x => x.Id);
                    table.CheckConstraint("CK_LibraryGames_PlaytimeHours", "[PlaytimeHours] >= 0 AND [PlaytimeHours] <= 100000");
                    table.CheckConstraint("CK_LibraryGames_Rating", "[Rating] IS NULL OR ([Rating] >= 1 AND [Rating] <= 10)");
                    table.CheckConstraint("CK_LibraryGames_Status", "[Status] >= 0 AND [Status] <= 5");
                    table.ForeignKey(
                        name: "FK_LibraryGames_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LibraryGames_Platforms_PlatformId",
                        column: x => x.PlatformId,
                        principalTable: "Platforms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RateLimitCounters",
                columns: table => new
                {
                    Key = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    WindowStartUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateLimitCounters", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "UserSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Ticket = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LibraryGameGenres",
                columns: table => new
                {
                    LibraryGameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GenreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LibraryGameGenres", x => new { x.LibraryGameId, x.GenreId });
                    table.ForeignKey(
                        name: "FK_LibraryGameGenres_Genres_GenreId",
                        column: x => x.GenreId,
                        principalTable: "Genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LibraryGameGenres_LibraryGames_LibraryGameId",
                        column: x => x.LibraryGameId,
                        principalTable: "LibraryGames",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryGameGenres_GenreId",
                table: "LibraryGameGenres",
                column: "GenreId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryGames_PlatformId",
                table: "LibraryGames",
                column: "PlatformId");

            migrationBuilder.CreateIndex(
                name: "IX_LibraryGames_UserId_DateAdded",
                table: "LibraryGames",
                columns: new[] { "UserId", "DateAdded" });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryGames_UserId_IsFavorite",
                table: "LibraryGames",
                columns: new[] { "UserId", "IsFavorite" });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryGames_UserId_Status",
                table: "LibraryGames",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LibraryGames_UserId_Title",
                table: "LibraryGames",
                columns: new[] { "UserId", "Title" });

            migrationBuilder.CreateIndex(
                name: "IX_RateLimitCounters_WindowStartUtc",
                table: "RateLimitCounters",
                column: "WindowStartUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_ExpiresAtUtc",
                table: "UserSessions",
                column: "ExpiresAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_UserId",
                table: "UserSessions",
                column: "UserId");

            migrationBuilder.Sql("""
                INSERT INTO [LibraryGames] ([Id], [UserId], [Title], [Description], [CoverImageUrl], [PlatformId], [Status],
                    [Rating], [PlaytimeHours], [IsFavorite], [Notes], [StartDate], [CompletionDate], [DateAdded], [UpdatedAtUtc])
                SELECT ug.[Id], ug.[UserId], g.[Title], LEFT(g.[Description], 4000), g.[CoverImageUrl], ug.[PlatformId],
                    CASE WHEN ug.[Status] BETWEEN 0 AND 5 THEN ug.[Status] ELSE 0 END,
                    CASE WHEN ug.[Rating] BETWEEN 1 AND 10 THEN ug.[Rating] ELSE NULL END,
                    CASE WHEN ug.[PlaytimeHours] < 0 THEN 0 WHEN ug.[PlaytimeHours] > 100000 THEN 100000 ELSE ug.[PlaytimeHours] END,
                    ug.[IsFavorite], ug.[Notes], CAST(ug.[StartDate] AS date), CAST(ug.[CompletionDate] AS date),
                    ug.[DateAdded], ug.[DateAdded]
                FROM [UserGames] ug
                INNER JOIN [Games] g ON g.[Id] = ug.[GameId]
                WHERE EXISTS (SELECT 1 FROM [AspNetUsers] u WHERE u.[Id] = ug.[UserId]);

                INSERT INTO [LibraryGameGenres] ([LibraryGameId], [GenreId])
                SELECT lg.[Id], gg.[GenreId]
                FROM [LibraryGames] lg
                INNER JOIN [UserGames] ug ON ug.[Id] = lg.[Id]
                INNER JOIN [GameGenres] gg ON gg.[GameId] = ug.[GameId];

                -- Arşiv: eski tabloların birebir kopyası (kısıt ve indeks olmadan). Yeni kurulumlarda eski
                -- tablolar boştur; o zaman boş arşiv tabloları oluşturulmaz.
                IF EXISTS (SELECT 1 FROM [Games]) OR EXISTS (SELECT 1 FROM [UserGames])
                BEGIN
                    SELECT * INTO [Legacy_Games] FROM [Games];
                    SELECT * INTO [Legacy_GameGenres] FROM [GameGenres];
                    SELECT * INTO [Legacy_UserGames] FROM [UserGames];
                END;
                """);

            migrationBuilder.DropTable(
                name: "GameGenres");

            migrationBuilder.DropTable(
                name: "UserGames");

            migrationBuilder.DropTable(
                name: "Games");

            // Başvuru verisi (üretimde de gerekir; demo verisi değildir). Ada göre idempotent eklenir,
            // mevcut kayıtlara dokunulmaz.
            migrationBuilder.Sql("""
                INSERT INTO [Platforms] ([Name])
                SELECT v.[Name] FROM (VALUES
                    (N'PC'), (N'PlayStation 5'), (N'PlayStation 4'), (N'Xbox Series X|S'), (N'Xbox One'),
                    (N'Nintendo Switch'), (N'Nintendo Switch 2'), (N'Mobil'), (N'Diğer')
                ) AS v([Name])
                WHERE NOT EXISTS (SELECT 1 FROM [Platforms] p WHERE p.[Name] = v.[Name]);

                INSERT INTO [Genres] ([Name])
                SELECT v.[Name] FROM (VALUES
                    (N'Aksiyon'), (N'Macera'), (N'Rol Yapma (RPG)'), (N'Strateji'), (N'Nişancı'), (N'Yarış'),
                    (N'Spor'), (N'Simülasyon'), (N'Bulmaca'), (N'Platform'), (N'Korku'), (N'Dövüş'),
                    (N'Roguelike'), (N'Hayatta Kalma'), (N'Sandbox')
                ) AS v([Name])
                WHERE NOT EXISTS (SELECT 1 FROM [Genres] g WHERE g.[Name] = v.[Name]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Geri alma: her LibraryGames kaydı için eski yapıda bir Games + UserGames kaydı üretilir.
            // Arşiv tabloları varsa kütüphaneye bağlı olmayan katalog oyunları ve çıkış tarihleri de geri yüklenir.
            // Eski şemada olmayan bilgiler kaybolur: görünen ad, oturumlar, e-posta kuyruğu, "Ara verdim"/"İstek listesi"
            // durumları (Backlog'a döner) ve 2000 karakteri aşan notların devamı.

            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CoverImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReleaseDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GameGenres",
                columns: table => new
                {
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GenreId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameGenres", x => new { x.GameId, x.GenreId });
                    table.ForeignKey(
                        name: "FK_GameGenres_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GameGenres_Genres_GenreId",
                        column: x => x.GenreId,
                        principalTable: "Genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserGames",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GameId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlatformId = table.Column<int>(type: "int", nullable: false),
                    CompletionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DateAdded = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsFavorite = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PlaytimeHours = table.Column<int>(type: "int", nullable: false),
                    Rating = table.Column<int>(type: "int", nullable: true),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserGames_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserGames_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserGames_Platforms_PlatformId",
                        column: x => x.PlatformId,
                        principalTable: "Platforms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameGenres_GenreId",
                table: "GameGenres",
                column: "GenreId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGames_GameId",
                table: "UserGames",
                column: "GameId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGames_PlatformId",
                table: "UserGames",
                column: "PlatformId");

            migrationBuilder.CreateIndex(
                name: "IX_UserGames_UserId_GameId_PlatformId",
                table: "UserGames",
                columns: new[] { "UserId", "GameId", "PlatformId" },
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO [Games] ([Id], [Title], [Description], [ReleaseDate], [CoverImageUrl])
                SELECT lg.[Id], lg.[Title], lg.[Description], NULL, lg.[CoverImageUrl] FROM [LibraryGames] lg;

                INSERT INTO [UserGames] ([Id], [UserId], [GameId], [PlatformId], [Status], [Rating], [PlaytimeHours],
                    [IsFavorite], [Notes], [DateAdded], [StartDate], [CompletionDate])
                SELECT lg.[Id], lg.[UserId], lg.[Id], lg.[PlatformId],
                    CASE WHEN lg.[Status] BETWEEN 0 AND 3 THEN lg.[Status] ELSE 0 END,
                    lg.[Rating], lg.[PlaytimeHours], lg.[IsFavorite], LEFT(lg.[Notes], 2000), lg.[DateAdded],
                    CAST(lg.[StartDate] AS datetime2), CAST(lg.[CompletionDate] AS datetime2)
                FROM [LibraryGames] lg;

                INSERT INTO [GameGenres] ([GameId], [GenreId])
                SELECT [LibraryGameId], [GenreId] FROM [LibraryGameGenres];

                IF OBJECT_ID(N'[Legacy_Games]') IS NOT NULL AND OBJECT_ID(N'[Legacy_UserGames]') IS NOT NULL
                BEGIN
                    EXEC(N'
                        UPDATE g SET g.[ReleaseDate] = old.[ReleaseDate]
                        FROM [Games] g
                        INNER JOIN [Legacy_UserGames] lu ON lu.[Id] = g.[Id]
                        INNER JOIN [Legacy_Games] old ON old.[Id] = lu.[GameId];

                        INSERT INTO [Games] ([Id], [Title], [Description], [ReleaseDate], [CoverImageUrl])
                        SELECT old.[Id], old.[Title], old.[Description], old.[ReleaseDate], old.[CoverImageUrl]
                        FROM [Legacy_Games] old
                        WHERE NOT EXISTS (SELECT 1 FROM [Legacy_UserGames] lu INNER JOIN [Games] m ON m.[Id] = lu.[Id] WHERE lu.[GameId] = old.[Id])
                          AND NOT EXISTS (SELECT 1 FROM [Games] g WHERE g.[Id] = old.[Id]);');

                    IF OBJECT_ID(N'[Legacy_GameGenres]') IS NOT NULL
                        EXEC(N'
                            INSERT INTO [GameGenres] ([GameId], [GenreId])
                            SELECT gg.[GameId], gg.[GenreId] FROM [Legacy_GameGenres] gg
                            WHERE NOT EXISTS (SELECT 1 FROM [Legacy_UserGames] lu INNER JOIN [Games] m ON m.[Id] = lu.[Id] WHERE lu.[GameId] = gg.[GameId])
                              AND EXISTS (SELECT 1 FROM [Games] g WHERE g.[Id] = gg.[GameId])
                              AND EXISTS (SELECT 1 FROM [Genres] ge WHERE ge.[Id] = gg.[GenreId])
                              AND NOT EXISTS (SELECT 1 FROM [GameGenres] x WHERE x.[GameId] = gg.[GameId] AND x.[GenreId] = gg.[GenreId]);');
                END;

                DROP TABLE IF EXISTS [Legacy_GameGenres];
                DROP TABLE IF EXISTS [Legacy_UserGames];
                DROP TABLE IF EXISTS [Legacy_Games];
                """);

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "LibraryGameGenres");

            migrationBuilder.DropTable(
                name: "RateLimitCounters");

            migrationBuilder.DropTable(
                name: "UserSessions");

            migrationBuilder.DropTable(
                name: "LibraryGames");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "AspNetUsers");
        }
    }
}
