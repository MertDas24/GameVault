using GameVault.Business.Library;
using GameVault.DataAccess;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Web.Infrastructure;

/// <summary>Yalnızca yerel geliştirme için komutlar. Üretimde hiçbiri çalışmaz.</summary>
public static partial class DevCommands
{
    /// <summary>Development'ta, açıkça etkinleştirildiyse bekleyen migration'ları uygular.</summary>
    public static async Task ApplyMigrationsIfEnabledAsync(WebApplication app)
    {
        if (!app.Environment.IsDevelopment() || !app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
        {
            return;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>();
        await db.Database.MigrateAsync();
    }

    /// <summary>
    /// `dotnet run --project GameVault.Web -- seed-demo` ile kurgusal demo hesabı ve oyunları
    /// oluşturur. Parola koda gömülmez: Seed:DemoPassword (user-secrets ya da ortam değişkeni) gerekir.
    /// </summary>
    public static async Task<int> SeedDemoAsync(WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("GameVault.Seed");
        if (!app.Environment.IsDevelopment())
        {
            LogRefused(logger, app.Environment.EnvironmentName);
            return 1;
        }

        var password = app.Configuration["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            LogMissingPassword(logger);
            return 1;
        }

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>();
        await db.Database.MigrateAsync();

        var seeder = scope.ServiceProvider.GetRequiredService<DemoDataSeeder>();
        var created = await seeder.SeedAsync(password);
        LogSeeded(logger, created ? "oluşturuldu" : "zaten vardı", DemoDataSeeder.DemoEmail);
        return 0;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "seed-demo yalnızca Development ortamında çalışır (şu an: {Environment}).")]
    private static partial void LogRefused(ILogger logger, string environment);

    [LoggerMessage(Level = LogLevel.Error, Message = "Seed:DemoPassword tanımlı değil. Örn: dotnet user-secrets set \"Seed:DemoPassword\" \"<parola>\" --project GameVault.Web")]
    private static partial void LogMissingPassword(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo verisi {Result}: {Email}")]
    private static partial void LogSeeded(ILogger logger, string result, string email);
}
