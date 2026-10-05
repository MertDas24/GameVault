using System.Text;
using GameVault.Business.Email;
using GameVault.Entities;
using GameVault.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Tests;

/// <summary>Belirli bir SMTP davranışıyla ve isteğe bağlı olarak paylaşılan veritabanıyla çalışan uygulama.</summary>
public sealed class OutboxFactory : GameVaultFactory
{
    private readonly IEmailSender _sender;
    private readonly int _maxAttempts;

    public OutboxFactory(IEmailSender sender, int maxAttempts = 6, string? sharedConnectionString = null)
        : base(sharedConnectionString ?? TestDatabase.CreateConnectionString(), ownsDatabase: sharedConnectionString is null)
    {
        _sender = sender;
        _maxAttempts = maxAttempts;
    }

    protected override IEmailSender Sender => _sender;

    protected override IDictionary<string, string?> Settings
    {
        get
        {
            var settings = base.Settings;
            settings["EmailOutbox:MaxAttempts"] = _maxAttempts.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return settings;
        }
    }
}

public sealed class OutboxTests
{
    private static async Task<T> EventuallyAsync<T>(Func<Task<T>> probe, Func<T, bool> done, int timeoutMs = 20_000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        T value;
        do
        {
            value = await probe();
            if (done(value))
            {
                return value;
            }

            await Task.Delay(100);
        }
        while (DateTime.UtcNow < deadline);

        return value;
    }

    private static async Task<Guid> EnqueueAsync(GameVaultFactory factory, string to, string body, TimeSpan validFor, string? dedupKey = null)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<EmailOutbox>();
        Assert.True(await outbox.EnqueueAsync(new EmailMessage(to, "Test", body), "test", null, validFor, dedupKey));
        return await factory.WithDbAsync(db => db.OutboxEmails.Where(o => o.DedupKey == dedupKey || dedupKey == null).OrderByDescending(o => o.CreatedAtUtc).Select(o => o.Id).FirstAsync());
    }

    [Fact]
    public async Task Transient_smtp_failures_are_retried_with_backoff_and_content_is_removed_after_sending()
    {
        var sender = new FlakyEmailSender(failures: 2);
        await using var factory = new OutboxFactory(sender);
        await factory.InitializeAsync();

        var id = await EnqueueAsync(factory, "retry@example.test", "gizli-bağlantı-123", TimeSpan.FromMinutes(5));
        var job = await EventuallyAsync(
            () => factory.WithDbAsync(db => db.OutboxEmails.AsNoTracking().SingleAsync(o => o.Id == id)),
            j => j.Status != OutboxStatus.Pending);

        Assert.Equal(OutboxStatus.Sent, job.Status);
        Assert.Equal(3, job.Attempts);
        Assert.Equal(3, sender.Calls);
        Assert.Null(job.ProtectedBody);
        Assert.Null(job.Recipient);
        Assert.Single(sender.Messages);
        Assert.Equal($"<{id:N}@gamevault.local>", sender.Messages.Single().MessageId);
    }

    [Fact]
    public async Task Permanently_failing_jobs_stop_after_max_attempts_and_are_tracked_without_content()
    {
        var sender = new FailingEmailSender();
        await using var factory = new OutboxFactory(sender, maxAttempts: 2);
        await factory.InitializeAsync();

        var id = await EnqueueAsync(factory, "fail@example.test", "gizli-bağlantı-456", TimeSpan.FromMinutes(5));
        var job = await EventuallyAsync(
            () => factory.WithDbAsync(db => db.OutboxEmails.AsNoTracking().SingleAsync(o => o.Id == id)),
            j => j.Status != OutboxStatus.Pending);

        Assert.Equal(OutboxStatus.Failed, job.Status);
        Assert.Equal(2, job.Attempts);
        Assert.Equal("IOException", job.LastError);
        Assert.Null(job.ProtectedBody);
        Assert.Null(job.Recipient);
        await Task.Delay(1500);
        Assert.Equal(2, sender.Calls);
    }

    [Fact]
    public async Task Pending_emails_survive_an_application_restart()
    {
        var connection = TestDatabase.CreateConnectionString();
        var options = new DbContextOptionsBuilder<GameVault.DataAccess.GameVaultDbContext>().UseSqlServer(connection).Options;
        await using (var db = new GameVault.DataAccess.GameVaultDbContext(options))
        {
            await db.Database.MigrateAsync();
        }

        try
        {
            // 1. instance: SMTP erişilemiyor; iş denenir ama gönderilemez.
            var failing = new FailingEmailSender();
            var first = new OutboxFactory(failing, maxAttempts: 20, sharedConnectionString: connection);
            await first.InitializeAsync();
            var id = await EnqueueAsync(first, "restart@example.test", "yeniden başlatma testi", TimeSpan.FromMinutes(5));
            await EventuallyAsync(() => Task.FromResult(failing.Calls), c => c >= 1);
            await first.DisposeAsync(); // uygulama durur, veritabanı kalır

            await using (var db = new GameVault.DataAccess.GameVaultDbContext(options))
            {
                var pending = await db.OutboxEmails.AsNoTracking().SingleAsync(o => o.Id == id);
                Assert.Equal(OutboxStatus.Pending, pending.Status);
                Assert.NotNull(pending.ProtectedBody);
            }

            // 2. instance (yeniden başlatma): bekleyen iş kaybolmadan gönderilir.
            var sender = new CapturingEmailSender();
            await using var second = new OutboxFactory(sender, sharedConnectionString: connection);
            await second.InitializeAsync();
            var sent = await EventuallyAsync(
                () => second.WithDbAsync(db => db.OutboxEmails.AsNoTracking().SingleAsync(o => o.Id == id)),
                j => j.Status == OutboxStatus.Sent);
            Assert.Equal(OutboxStatus.Sent, sent.Status);
            Assert.Equal("yeniden başlatma testi", Assert.Single(sender.Messages).TextBody);
        }
        finally
        {
            await using var db = new GameVault.DataAccess.GameVaultDbContext(options);
            await db.Database.EnsureDeletedAsync();
        }
    }

    [Fact]
    public async Task Expired_emails_are_not_sent()
    {
        var sender = new CapturingEmailSender();
        await using var factory = new OutboxFactory(sender);
        await factory.InitializeAsync();

        var id = await EnqueueAsync(factory, "late@example.test", "süresi dolmuş bağlantı", TimeSpan.FromSeconds(-1));
        var job = await EventuallyAsync(
            () => factory.WithDbAsync(db => db.OutboxEmails.AsNoTracking().SingleAsync(o => o.Id == id)),
            j => j.Status != OutboxStatus.Pending);

        Assert.Equal(OutboxStatus.Expired, job.Status);
        Assert.Empty(sender.Messages);
        Assert.Null(job.ProtectedBody);
    }

    [Fact]
    public async Task Duplicate_jobs_are_suppressed_and_bodies_are_encrypted_at_rest()
    {
        var failing = new FailingEmailSender();
        await using var factory = new OutboxFactory(failing, maxAttempts: 20);
        await factory.InitializeAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var outbox = scope.ServiceProvider.GetRequiredService<EmailOutbox>();
        const string secret = "token=COK-GIZLI-DEGER";
        Assert.True(await outbox.EnqueueAsync(new EmailMessage("dup@example.test", "Test", secret), "test", null, TimeSpan.FromMinutes(5), "dup-key"));
        Assert.False(await outbox.EnqueueAsync(new EmailMessage("dup@example.test", "Test", secret), "test", null, TimeSpan.FromMinutes(5), "dup-key"));

        var rows = await factory.WithDbAsync(db => db.OutboxEmails.AsNoTracking().Where(o => o.DedupKey == "dup-key").ToListAsync());
        var row = Assert.Single(rows);
        Assert.NotNull(row.ProtectedBody);
        Assert.DoesNotContain("COK-GIZLI", Encoding.UTF8.GetString(row.ProtectedBody!), StringComparison.Ordinal);
        Assert.DoesNotContain("COK-GIZLI", Convert.ToBase64String(row.ProtectedBody!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logs_do_not_contain_recipients_tokens_or_passwords()
    {
        await using var factory = new OutboxFactory(new FlakyEmailSender(failures: 1));
        await factory.InitializeAsync();
        var email = $"log-{Guid.NewGuid():N}@example.test";
        using var client = factory.CreateAppClient();
        await client.RegisterAsync(email, password: "log-testi-parolasi-9");
        await client.PostFormAsync("/Account/ForgotPassword", new Dictionary<string, string> { ["Input.Email"] = email });
        var reset = await factory.Emails.WaitForAsync(email, "parola sıfırlama");
        var token = reset.TextBody.Split("token=")[1].Split('\n')[0].Trim();

        var logs = string.Join('\n', factory.Logs.Lines);
        Assert.Contains("reset-password", logs, StringComparison.Ordinal); // gönderim gerçekten loglandı
        Assert.DoesNotContain(email, logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(token, logs, StringComparison.Ordinal);
        Assert.DoesNotContain("log-testi-parolasi-9", logs, StringComparison.Ordinal);
    }
}
