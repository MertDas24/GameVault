using Microsoft.Data.SqlClient;

namespace GameVault.Tests.Infrastructure;

/// <summary>
/// Her test sınıfı için ayrı, geçici bir SQL Server veritabanı adı üretir.
/// Sunucu: GAMEVAULT_TEST_SQLSERVER ortam değişkeni (veritabanı adı içermeyen bağlantı dizesi);
/// yoksa yerel LocalDB. Testler asla üretim veritabanına bağlanmamalıdır.
/// </summary>
public static class TestDatabase
{
    private const string DefaultServer = @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";

    public static string CreateConnectionString(string? databaseName = null)
    {
        var server = Environment.GetEnvironmentVariable("GAMEVAULT_TEST_SQLSERVER");
        var builder = new SqlConnectionStringBuilder(string.IsNullOrWhiteSpace(server) ? DefaultServer : server)
        {
            InitialCatalog = databaseName ?? $"GameVaultTest_{Guid.NewGuid():N}"[..28],
            MultipleActiveResultSets = false
        };

        if (!builder.InitialCatalog.StartsWith("GameVaultTest_", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Test veritabanı adları 'GameVaultTest_' ile başlamalı.");
        }

        return builder.ConnectionString;
    }
}
