using GameVault.Business.Library;
using GameVault.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace GameVault.E2E;

/// <summary>
/// Uygulamayı gerçek bir Kestrel sunucusunda (rastgele port) ve geçici bir veritabanıyla
/// başlatır, Chromium'u headless açar. Tarayıcı kurulumu: README > Testler.
/// </summary>
public sealed class E2EFixture : IAsyncLifetime
{
    private readonly KestrelFactory _factory = new();
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;
    public CapturingEmailSender Emails => _factory.Emails;
    public string BaseUrl { get; private set; } = string.Empty;
    public static string ArtifactsDir { get; } = Path.Combine(AppContext.BaseDirectory, "e2e-artifacts");

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(ArtifactsDir);
        _factory.UseKestrel(0);
        await _factory.InitializeAsync(); // Services erişimi Kestrel sunucusunu başlatır ve veritabanını hazırlar
        BaseUrl = _factory.ClientOptions.BaseAddress.ToString().TrimEnd('/');

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    /// <summary>Geçici test veritabanına kurgusal demo hesabını ve oyunlarını ekler.</summary>
    public async Task SeedDemoAsync(string password)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<DemoDataSeeder>().SeedAsync(password);
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.DisposeAsync();
        }

        _playwright?.Dispose();
        await _factory.DisposeAsync();
    }

    private sealed class KestrelFactory : GameVaultFactory
    {
        protected override IDictionary<string, string?> Settings
        {
            get
            {
                var settings = base.Settings;
                // Çözümlenemeyen bir alan adı: bozuk kapak görselinin yedek görünümünü test etmek için.
                settings["Covers:AllowedHosts:1"] = "cdn.gamevault.invalid";
                return settings;
            }
        }
    }
}
