using System.Net;
using GameVault.Business.Security;
using GameVault.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Tests;

/// <summary>Düşük sınırlarla yapılandırılmış uygulama.</summary>
public class LowLimitFactory : GameVaultFactory
{
    public LowLimitFactory()
    {
    }

    protected LowLimitFactory(string connectionString) : base(connectionString, ownsDatabase: false)
    {
    }

    protected override IDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["RateLimiting:LoginPerIp:PermitLimit"] = "100",
        ["RateLimiting:LoginPerAccount:PermitLimit"] = "3",
        ["RateLimiting:RegisterPerIp:PermitLimit"] = "100",
        ["RateLimiting:ForgotPasswordPerIp:PermitLimit"] = "100",
        ["RateLimiting:ForgotPasswordPerAccount:PermitLimit"] = "2",
        ["RateLimiting:ExportPerUser:PermitLimit"] = "2",
        ["RateLimiting:SensitivePerUser:PermitLimit"] = "100",
        ["RateLimiting:ResetPasswordPerIp:PermitLimit"] = "100",
        ["RateLimiting:ConfirmationPerIp:PermitLimit"] = "100",
        ["RateLimiting:ConfirmationPerAccount:PermitLimit"] = "2",
        ["EmailOutbox:PollSeconds"] = "1"
    };

    /// <summary>Aynı veritabanını kullanan ikinci bir uygulama örneği (çoklu instance benzetimi).</summary>
    public LowLimitFactory CreateSecondInstance() => new SecondInstance(ConnectionString);

    private sealed class SecondInstance(string connectionString) : LowLimitFactory(connectionString);
}

public sealed class RateLimitTests(LowLimitFactory factory) : IClassFixture<LowLimitFactory>
{
    [Fact]
    public async Task Login_attempts_per_account_are_limited_even_for_unknown_accounts()
    {
        using var client = factory.CreateAppClient();
        var email = $"nobody-{Guid.NewGuid():N}@example.test";

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.LoginAsync(email, "yanlis")).StatusCode);
        }

        var limited = await client.LoginAsync(email, "yanlis");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Contains("Çok fazla deneme", await limited.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Limit_is_shared_between_app_instances_using_the_same_database()
    {
        await using var second = factory.CreateSecondInstance();
        using var clientA = factory.CreateAppClient();
        using var clientB = second.CreateAppClient();
        var email = $"shared-{Guid.NewGuid():N}@example.test";

        Assert.Equal(HttpStatusCode.OK, (await clientA.LoginAsync(email, "yanlis")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clientB.LoginAsync(email, "yanlis")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await clientA.LoginAsync(email, "yanlis")).StatusCode);

        // Dördüncü deneme hangi instance'a giderse gitsin reddedilir.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await clientB.LoginAsync(email, "yanlis")).StatusCode);
    }

    [Fact]
    public async Task Forgot_password_requests_are_limited_per_account()
    {
        var email = $"forgot-{Guid.NewGuid():N}@example.test";
        using var client = factory.CreateAppClient();
        await client.RegisterAsync(email);
        await client.LogoutAsync();

        for (var i = 0; i < 2; i++)
        {
            var ok = await client.PostFormAsync("/Account/ForgotPassword", new Dictionary<string, string> { ["Input.Email"] = email });
            Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        }

        var limited = await client.PostFormAsync("/Account/ForgotPassword", new Dictionary<string, string> { ["Input.Email"] = email });
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        // İlk iki istek aynı tekrar-önleme penceresinde olduğundan tek e-posta kuyruğa girer (bkz. OutboxTests).
        await factory.Emails.WaitForAsync(email, "parola sıfırlama");
        await Task.Delay(1500);
        Assert.Equal(1, factory.Emails.CountFor(email, "parola sıfırlama"));
    }

    [Fact]
    public async Task Resend_confirmation_is_limited_per_account_identically_for_unknown_addresses()
    {
        var email = $"resend-{Guid.NewGuid():N}@example.test";
        var unknown = $"resend-nobody-{Guid.NewGuid():N}@example.test";
        using var client = factory.CreateAppClient();
        await client.SubmitRegistrationAsync(email);
        await factory.Emails.WaitForAsync(email, "doğrula");

        foreach (var address in new[] { email, unknown })
        {
            for (var i = 0; i < 2; i++)
            {
                var ok = await client.PostFormAsync("/Account/ResendConfirmation", new Dictionary<string, string> { ["Input.Email"] = address });
                Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
            }

            var limited = await client.PostFormAsync("/Account/ResendConfirmation", new Dictionary<string, string> { ["Input.Email"] = address });
            Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
            Assert.Contains("Çok fazla deneme", await limited.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        // Kayıt + iki yeniden gönderim: 2 dakikalık tekrar-önleme penceresi nedeniyle en fazla iki e-posta
        // (pencere sınırına denk gelirse). Sınırlanan istek yeni e-posta üretmez.
        await Task.Delay(1500);
        Assert.InRange(factory.Emails.CountFor(email, "doğrula"), 1, 2);
        Assert.Equal(0, factory.Emails.CountFor(unknown));
    }

    [Fact]
    public async Task Export_is_limited_per_user()
    {
        using var client = factory.CreateAppClient();
        await client.RegisterAsync($"export-{Guid.NewGuid():N}@example.test");

        Assert.Equal(HttpStatusCode.OK, (await client.PostFormAsync("/Account/Export", [])).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostFormAsync("/Account/Export", [])).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostFormAsync("/Account/Export", [])).StatusCode);
    }

    [Fact]
    public async Task Counter_resets_in_a_new_window()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var limiter = scope.ServiceProvider.GetRequiredService<DbRateLimiter>();
        var rule = new RateLimitRule(2, TimeSpan.FromSeconds(2));
        var partition = Guid.NewGuid().ToString();

        // Pencere sınırına yakın başlamamak için bir sonraki pencerenin başını bekle.
        var now = DateTime.UtcNow;
        var windowTicks = TimeSpan.FromSeconds(2).Ticks;
        await Task.Delay(TimeSpan.FromTicks(windowTicks - (now.Ticks % windowTicks)) + TimeSpan.FromMilliseconds(50));

        Assert.True(await limiter.TryAcquireAsync("test", rule, partition));
        Assert.True(await limiter.TryAcquireAsync("test", rule, partition));
        Assert.False(await limiter.TryAcquireAsync("test", rule, partition));

        await Task.Delay(TimeSpan.FromSeconds(2.1));
        Assert.True(await limiter.TryAcquireAsync("test", rule, partition));
    }
}
