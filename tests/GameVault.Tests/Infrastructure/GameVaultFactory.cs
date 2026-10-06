using GameVault.Business.Email;
using GameVault.DataAccess;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace GameVault.Tests.Infrastructure;

/// <summary>
/// Uygulamayı gerçek pipeline'ı ve gerçek SQL Server veritabanıyla (geçici, sınıfa özel)
/// bellek içinde çalıştırır. E-postalar yakalanır; oran sınırları varsayılan olarak yüksektir.
/// </summary>
public class GameVaultFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly bool _ownsDatabase;

    public GameVaultFactory() : this(TestDatabase.CreateConnectionString(), ownsDatabase: true)
    {
    }

    protected GameVaultFactory(string connectionString, bool ownsDatabase)
    {
        ConnectionString = connectionString;
        _ownsDatabase = ownsDatabase;
    }

    public string ConnectionString { get; }

    private readonly CapturingEmailSender _defaultEmails = new();

    /// <summary>Yakalanan e-postalar (gönderici bir CapturingEmailSender ise onun kayıtları).</summary>
    public CapturingEmailSender Emails => Sender as CapturingEmailSender ?? _defaultEmails;

    public CapturingLoggerProvider Logs { get; } = new();

    /// <summary>Gönderimde kullanılacak SMTP; varsayılan olarak <see cref="Emails"/>.</summary>
    protected virtual IEmailSender Sender => _defaultEmails;

    /// <summary>Alt sınıflar oran sınırı gibi ayarları değiştirebilir.</summary>
    protected virtual IDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["RateLimiting:LoginPerIp:PermitLimit"] = "10000",
        ["RateLimiting:LoginPerAccount:PermitLimit"] = "10000",
        ["RateLimiting:RegisterPerIp:PermitLimit"] = "10000",
        ["RateLimiting:ForgotPasswordPerIp:PermitLimit"] = "10000",
        ["RateLimiting:ForgotPasswordPerAccount:PermitLimit"] = "10000",
        ["RateLimiting:ResetPasswordPerIp:PermitLimit"] = "10000",
        ["RateLimiting:ConfirmationPerIp:PermitLimit"] = "10000",
        ["RateLimiting:ConfirmationPerAccount:PermitLimit"] = "10000",
        ["RateLimiting:SensitivePerUser:PermitLimit"] = "10000",
        ["RateLimiting:ExportPerUser:PermitLimit"] = "10000",
        ["RateLimiting:ImportPerUser:PermitLimit"] = "10000",
        ["EmailOutbox:PollSeconds"] = "1",
        ["EmailOutbox:BaseDelaySeconds"] = "1",
        ["EmailOutbox:MaxDelaySeconds"] = "2"
    };

    protected virtual void ConfigureServices(IServiceCollection services)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);
        builder.UseSetting("App:PublicBaseUrl", "http://localhost");
        builder.UseSetting("Email:Host", "localhost");
        builder.UseSetting("Email:Port", "1025");
        builder.UseSetting("Email:Security", "None");
        builder.UseSetting("Email:FromAddress", "no-reply@gamevault.test");
        builder.UseSetting("Covers:AllowedHosts:0", "images.example.com");
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureLogging(logging => logging.AddProvider(Logs).SetMinimumLevel(LogLevel.Debug));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton(Sender);
            ConfigureServices(services);
        });
    }

    public async ValueTask InitializeAsync()
    {
        if (!_ownsDatabase)
        {
            _ = Services; // sunucuyu başlat
            return;
        }

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public override async ValueTask DisposeAsync()
    {
        if (_ownsDatabase)
        {
            try
            {
                await using var scope = Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>();
                await db.Database.EnsureDeletedAsync();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        await base.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    public AppClient CreateAppClient() =>
        new(CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true }), Emails);

    /// <summary>Çerezleri otomatik yönetmeyen istemci (çerez yeniden oynatma testleri için).</summary>
    public HttpClient CreateRawClient() =>
        CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });

    public async Task WithDbAsync(Func<GameVaultDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<GameVaultDbContext>());
    }

    public async Task<T> WithDbAsync<T>(Func<GameVaultDbContext, Task<T>> action)
    {
        await using var scope = Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<GameVaultDbContext>());
    }
}
