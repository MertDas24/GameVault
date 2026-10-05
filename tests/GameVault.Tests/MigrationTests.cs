using GameVault.DataAccess;
using GameVault.Entities;
using GameVault.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace GameVault.Tests;

/// <summary>
/// Eski şemadaki (global katalog) verilerin UserOwnedLibrary migration'ı ile kaybolmadan
/// kullanıcıya ait kayıtlara taşındığını ve geri alma (Down) yolunun çalıştığını doğrular.
/// </summary>
public sealed class MigrationTests : IAsyncLifetime
{
    private const string LegacyMigration = "20260825160808_AddIdentityTables";
    private readonly string _connectionString = TestDatabase.CreateConnectionString();
    private GameVaultDbContext _db = null!;

    public async ValueTask InitializeAsync()
    {
        _db = new GameVaultDbContext(new DbContextOptionsBuilder<GameVaultDbContext>().UseSqlServer(_connectionString).Options);
        await _db.Database.EnsureDeletedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    [Fact]
    public async Task Model_has_no_pending_changes()
    {
        Assert.False(_db.Database.HasPendingModelChanges());
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Fresh_install_creates_no_empty_archive_tables()
    {
        await _db.GetService<IMigrator>().MigrateAsync();
        Assert.Equal(0, await Scalar("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] LIKE 'Legacy[_]%'"));
    }

    [Fact]
    public async Task Upgrade_preserves_legacy_library_data_and_down_restores_it()
    {
        var migrator = _db.GetService<IMigrator>();
        await migrator.MigrateAsync(LegacyMigration);

        var userId = Guid.NewGuid();
        var gameId = Guid.NewGuid();
        var catalogOnlyGameId = Guid.NewGuid();
        var entryId = Guid.NewGuid();

        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [AspNetUsers] ([Id], [UserName], [NormalizedUserName], [Email], [NormalizedEmail], [EmailConfirmed],
                [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount])
            VALUES ({userId}, N'eski@example.test', N'ESKI@EXAMPLE.TEST', N'eski@example.test', N'ESKI@EXAMPLE.TEST', 0, 0, 0, 1, 0);
            SET IDENTITY_INSERT [Platforms] ON;
            INSERT INTO [Platforms] ([Id], [Name]) VALUES (50, N'PC');
            SET IDENTITY_INSERT [Platforms] OFF;
            SET IDENTITY_INSERT [Genres] ON;
            INSERT INTO [Genres] ([Id], [Name]) VALUES (70, N'Eski Tür');
            SET IDENTITY_INSERT [Genres] OFF;
            INSERT INTO [Games] ([Id], [Title], [Description], [ReleaseDate], [CoverImageUrl]) VALUES ({gameId}, N'Eski Oyun', N'Açıklama', '2020-05-01', NULL);
            INSERT INTO [Games] ([Id], [Title], [ReleaseDate]) VALUES ({catalogOnlyGameId}, N'Sahipsiz Katalog Oyunu', '2019-03-02');
            INSERT INTO [GameGenres] ([GameId], [GenreId]) VALUES ({catalogOnlyGameId}, 70);
            INSERT INTO [GameGenres] ([GameId], [GenreId]) VALUES ({gameId}, 70);
            INSERT INTO [UserGames] ([Id], [UserId], [GameId], [PlatformId], [Status], [Rating], [PlaytimeHours], [IsFavorite],
                [Notes], [DateAdded], [StartDate], [CompletionDate])
            VALUES ({entryId}, {userId}, {gameId}, 50, 2, 9, 30, 1, N'Eski not', '2026-08-26T10:00:00', '2026-08-01T00:00:00', NULL);
            """);

        await migrator.MigrateAsync();

        var migrated = await _db.LibraryGames.Include(g => g.Genres).Include(g => g.Platform).AsNoTracking().SingleAsync();
        Assert.Equal(entryId, migrated.Id);
        Assert.Equal(userId, migrated.UserId);
        Assert.Equal("Eski Oyun", migrated.Title);
        Assert.Equal("Açıklama", migrated.Description);
        Assert.Equal("PC", migrated.Platform.Name);
        Assert.Equal(GameStatus.Completed, migrated.Status);
        Assert.Equal(9, migrated.Rating);
        Assert.Equal(30, migrated.PlaytimeHours);
        Assert.True(migrated.IsFavorite);
        Assert.Equal("Eski not", migrated.Notes);
        Assert.Equal(new DateOnly(2026, 8, 1), migrated.StartDate);
        Assert.Equal("Eski Tür", Assert.Single(migrated.Genres).Name);
        Assert.Equal(new DateTime(2026, 8, 26, 10, 0, 0, DateTimeKind.Utc), migrated.DateAdded);

        // Mevcut "PC" tekrar eklenmez; eksik başvuru verileri eklenir.
        Assert.Equal(1, await _db.Platforms.CountAsync(p => p.Name == "PC"));
        Assert.True(await _db.Platforms.AnyAsync(p => p.Name == "Nintendo Switch"));
        Assert.True(await _db.Genres.AnyAsync(g => g.Name == "Rol Yapma (RPG)"));

        var user = await _db.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
        Assert.True(user.CreatedAtUtc > new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.False(user.EmailConfirmed); // eski hesaplar doğrulama politikasına tabidir (bkz. OPERATIONS.md)

        // Kütüphaneye dönüştürülemeyen veriler silinmez: eski tabloların tam kopyası arşivde kalır.
        Assert.Equal(2, await Scalar("SELECT COUNT(*) AS [Value] FROM [Legacy_Games]"));
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) AS [Value] FROM [Legacy_Games] WHERE [Id] = '{catalogOnlyGameId}' AND [ReleaseDate] = '2019-03-02'"));
        Assert.Equal(2, await Scalar("SELECT COUNT(*) AS [Value] FROM [Legacy_GameGenres]"));
        Assert.Equal(1, await Scalar("SELECT COUNT(*) AS [Value] FROM [Legacy_UserGames]"));

        // Geri alma: eski yapı ve veriler geri gelir.
        await migrator.MigrateAsync(LegacyMigration);
        var restoredTitle = await _db.Database.SqlQuery<string>($"""
            SELECT g.[Title] AS [Value] FROM [UserGames] ug INNER JOIN [Games] g ON g.[Id] = ug.[GameId] WHERE ug.[Id] = {entryId}
            """).SingleAsync();
        Assert.Equal("Eski Oyun", restoredTitle);
        var restoredGenres = await _db.Database.SqlQuery<int>($"SELECT COUNT(*) AS [Value] FROM [GameGenres] WHERE [GameId] = {entryId}").SingleAsync();
        Assert.Equal(1, restoredGenres);

        // Kütüphanede olmayan katalog oyunu ve çıkış tarihleri arşivden geri gelir; arşiv tabloları kaldırılır.
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) AS [Value] FROM [Games] WHERE [Id] = '{catalogOnlyGameId}' AND [ReleaseDate] = '2019-03-02'"));
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) AS [Value] FROM [GameGenres] WHERE [GameId] = '{catalogOnlyGameId}'"));
        Assert.Equal(1, await Scalar($"SELECT COUNT(*) AS [Value] FROM [Games] WHERE [Id] = '{entryId}' AND [ReleaseDate] = '2020-05-01'"));
        Assert.Equal(0, await Scalar("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE [name] LIKE 'Legacy[_]%'"));

        // Yeniden yükseltme de çalışır (arşiv yeniden oluşturulur).
        await migrator.MigrateAsync();
        Assert.Equal(2, await _db.LibraryGames.CountAsync() + await Scalar($"SELECT COUNT(*) AS [Value] FROM [Legacy_Games] WHERE [Id] = '{catalogOnlyGameId}'"));

    }

#pragma warning disable EF1002 // test verisi; kullanıcı girdisi yok
    private Task<int> Scalar(string sql) => _db.Database.SqlQueryRaw<int>(sql).SingleAsync();
#pragma warning restore EF1002
}
