using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GameVault.DataAccess;

/// <summary>
/// Yalnızca `dotnet ef` araçları için. Bağlantı dizesi GAMEVAULT_DESIGN_CONNECTION
/// ortam değişkeninden okunur; yoksa yerel LocalDB kullanılır. Uygulama çalışırken kullanılmaz.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GameVaultDbContext>
{
    public GameVaultDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("GAMEVAULT_DESIGN_CONNECTION")
            ?? @"Server=(localdb)\MSSQLLocalDB;Database=GameVaultDev;Trusted_Connection=True";

        var options = new DbContextOptionsBuilder<GameVaultDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new GameVaultDbContext(options);
    }
}
