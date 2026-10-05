using GameVault.DataAccess;
using GameVault.Tests.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Tests;

/// <summary>
/// docs/OPERATIONS.md içindeki BACKUP / RESTORE VERIFYONLY / RESTORE adımlarını geçici bir veritabanında
/// uygular: veri silindikten sonra yedekten geri yüklenebildiğini doğrular. Yedek dosyası SQL Server'ın
/// varsayılan yedek klasörüne yazılır (LocalDB'de yerel disk, CI'da konteyner içi).
/// </summary>
public sealed class BackupRestoreTests
{
    [Fact]
    public async Task Database_can_be_backed_up_verified_and_restored()
    {
        var connectionString = TestDatabase.CreateConnectionString();
        var dbName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString;
        var options = new DbContextOptionsBuilder<GameVaultDbContext>().UseSqlServer(connectionString).Options;
        string? backupFile = null;

        try
        {
            await using (var db = new GameVaultDbContext(options))
            {
                await db.Database.MigrateAsync();
                await db.Database.ExecuteSqlRawAsync("INSERT INTO [Genres] ([Name]) VALUES (N'Yedek Testi')");
            }

            await using var conn = new SqlConnection(master);
            await conn.OpenAsync();
            // LocalDB varsayılan yedek klasörü tanımlamaz; o durumda veri klasörü, o da yoksa geçici klasör kullanılır.
            var backupDir = await Exec(conn, "SELECT CAST(COALESCE(SERVERPROPERTY('InstanceDefaultBackupPath'), SERVERPROPERTY('InstanceDefaultDataPath')) AS nvarchar(4000))") as string
                ?? Path.GetTempPath();
            var separator = backupDir.Contains('/', StringComparison.Ordinal) ? "/" : "\\";
            backupFile = backupDir.TrimEnd('/', '\\') + separator + dbName + ".bak";

            await Exec(conn, $"BACKUP DATABASE [{dbName}] TO DISK = @p WITH INIT, CHECKSUM", backupFile);
            await Exec(conn, "RESTORE VERIFYONLY FROM DISK = @p WITH CHECKSUM", backupFile);

            // Veri kaybı benzetimi.
            await using (var db = new GameVaultDbContext(options))
            {
                await db.Database.ExecuteSqlRawAsync("DELETE FROM [Genres] WHERE [Name] = N'Yedek Testi'");
            }

            SqlConnection.ClearAllPools();
            await Exec(conn, $"ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await Exec(conn, $"RESTORE DATABASE [{dbName}] FROM DISK = @p WITH REPLACE, CHECKSUM", backupFile);
            await Exec(conn, $"ALTER DATABASE [{dbName}] SET MULTI_USER");

            await using (var db = new GameVaultDbContext(options))
            {
                Assert.True(await db.Genres.AnyAsync(g => g.Name == "Yedek Testi"));
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            }
        }
        finally
        {
            SqlConnection.ClearAllPools();
            await using (var db = new GameVaultDbContext(options))
            {
                await db.Database.EnsureDeletedAsync();
            }

            if (backupFile is not null && File.Exists(backupFile))
            {
                File.Delete(backupFile); // yalnızca sunucu yerel diskteyse (LocalDB)
            }
        }
    }

    private static async Task<object?> Exec(SqlConnection conn, string sql, string? parameter = null)
    {
        await using var cmd = conn.CreateCommand();
#pragma warning disable CA2100 // veritabanı adı test tarafından üretilir; dosya yolu parametredir
        cmd.CommandText = sql;
#pragma warning restore CA2100
        cmd.CommandTimeout = 120;
        if (parameter is not null)
        {
            cmd.Parameters.AddWithValue("@p", parameter);
        }

        return await cmd.ExecuteScalarAsync();
    }
}
