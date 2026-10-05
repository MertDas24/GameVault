using System.Security.Cryptography;
using System.Text;
using GameVault.DataAccess;
using GameVault.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameVault.Business.Email;

/// <summary>
/// Kalıcı outbox'taki e-postaları gönderir. Birden fazla instance aynı anda çalışabilir: işler
/// UPDLOCK/READPAST ile sahiplenilir ve kilit süresi dolana kadar diğerlerine görünmez.
/// Teslim garantisi "en az bir kez"dir: SMTP başarılı olup durum yazılamadan süreç çökerse aynı
/// e-posta (aynı Message-ID ile) bir kez daha gönderilebilir.
/// </summary>
public sealed partial class EmailDispatchService(
    IServiceScopeFactory scopes,
    IEmailSender sender,
    OutboxSignal signal,
    IOptions<EmailOutboxOptions> options,
    TimeProvider time,
    ILogger<EmailDispatchService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            int processed;
            try
            {
                processed = await ProcessBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogBatchFailed(logger, ex.GetType().Name);
                processed = 0;
            }

            if (processed == 0)
            {
                try
                {
                    await signal.WaitAsync(TimeSpan.FromSeconds(options.Value.PollSeconds), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    /// <returns>İşlenen iş sayısı.</returns>
    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        var o = options.Value;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector(EmailOutbox.ProtectorPurpose);

        var now = time.GetUtcNow().UtcDateTime;
        var lockUntil = now.AddSeconds(o.LockSeconds);

        // Deneme sayısı sahiplenirken artırılır: gönderim sırasında süreç çökerse iş sonsuza dek denenmez.
        var claimed = await db.Database.SqlQuery<Guid>($"""
            WITH c AS (
                SELECT TOP ({o.BatchSize}) * FROM [OutboxEmails] WITH (ROWLOCK, UPDLOCK, READPAST)
                WHERE [Status] = 0 AND [NextAttemptAtUtc] <= {now}
                  AND ([LockedUntilUtc] IS NULL OR [LockedUntilUtc] < {now})
                ORDER BY [NextAttemptAtUtc]
            )
            UPDATE c SET [LockedUntilUtc] = {lockUntil}, [Attempts] = [Attempts] + 1
            OUTPUT inserted.[Id] AS [Value];
            """).ToListAsync(ct);

        foreach (var id in claimed)
        {
            var job = await db.OutboxEmails.FirstAsync(x => x.Id == id, ct);
            await ProcessAsync(db, protector, job, ct);
        }

        return claimed.Count;
    }

    private async Task ProcessAsync(GameVaultDbContext db, IDataProtector protector, OutboxEmail job, CancellationToken ct)
    {
        var o = options.Value;
        var now = time.GetUtcNow().UtcDateTime;

        if (job.ExpiresAtUtc <= now)
        {
            Complete(job, OutboxStatus.Expired, now, "Expired");
            await db.SaveChangesAsync(ct);
            LogFinished(logger, job.Id, job.Kind, nameof(OutboxStatus.Expired), job.Attempts);
            return;
        }

        string body;
        try
        {
            body = Encoding.UTF8.GetString(protector.Unprotect(job.ProtectedBody ?? []));
        }
        catch (CryptographicException)
        {
            // Data Protection anahtarları kaybolduysa gövde okunamaz; yeniden denemek anlamsızdır.
            Complete(job, OutboxStatus.Failed, now, nameof(CryptographicException));
            await db.SaveChangesAsync(ct);
            LogFinished(logger, job.Id, job.Kind, nameof(OutboxStatus.Failed), job.Attempts);
            return;
        }

        try
        {
            await sender.SendAsync(new EmailMessage(job.Recipient!, job.Subject ?? string.Empty, body, $"<{job.Id:N}@gamevault.local>"), ct);
            Complete(job, OutboxStatus.Sent, time.GetUtcNow().UtcDateTime, null);
            await db.SaveChangesAsync(CancellationToken.None);
            LogFinished(logger, job.Id, job.Kind, nameof(OutboxStatus.Sent), job.Attempts);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (job.Attempts >= o.MaxAttempts)
            {
                Complete(job, OutboxStatus.Failed, now, Truncate(ex.GetType().Name));
                LogFinished(logger, job.Id, job.Kind, nameof(OutboxStatus.Failed), job.Attempts);
            }
            else
            {
                var delay = Math.Min(o.MaxDelaySeconds, o.BaseDelaySeconds * Math.Pow(2, job.Attempts - 1));
                job.NextAttemptAtUtc = now.AddSeconds(delay);
                job.LockedUntilUtc = null;
                job.LastError = Truncate(ex.GetType().Name);
                LogRetry(logger, job.Id, job.Kind, job.Attempts, ex.GetType().Name, delay);
            }

            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    /// <summary>İş sonlandığında alıcı ve içerik (token/bağlantı) silinir; yalnızca takip bilgisi kalır.</summary>
    private static void Complete(OutboxEmail job, OutboxStatus status, DateTime now, string? error)
    {
        job.Status = status;
        job.CompletedAtUtc = now;
        job.LockedUntilUtc = null;
        job.ProtectedBody = null;
        job.Recipient = null;
        job.Subject = null;
        job.LastError = error;
    }

    private static string Truncate(string value) => value.Length <= 100 ? value : value[..100];

    [LoggerMessage(Level = LogLevel.Error, Message = "E-posta kuyruğu işlenemedi ({ErrorType}).")]
    private static partial void LogBatchFailed(ILogger logger, string errorType);

    [LoggerMessage(Level = LogLevel.Warning, Message = "E-posta {JobId} ({Kind}) gönderilemedi, deneme {Attempt} ({ErrorType}); {DelaySeconds} sn sonra yeniden denenecek.")]
    private static partial void LogRetry(ILogger logger, Guid jobId, string kind, int attempt, string errorType, double delaySeconds);

    [LoggerMessage(Level = LogLevel.Information, Message = "E-posta {JobId} ({Kind}) sonlandı: {Status}, {Attempts} deneme.")]
    private static partial void LogFinished(ILogger logger, Guid jobId, string kind, string status, int attempts);
}
