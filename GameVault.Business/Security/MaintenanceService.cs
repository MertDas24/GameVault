using GameVault.Business.Email;
using GameVault.DataAccess;
using GameVault.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameVault.Business.Security;

/// <summary>Süresi dolmuş oturumları ve eski oran sınırı sayaçlarını periyodik olarak temizler.</summary>
public sealed partial class MaintenanceService(
    IServiceScopeFactory scopes,
    TimeProvider time,
    IOptions<EmailOutboxOptions> outboxOptions,
    ILogger<MaintenanceService> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, time);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogCleanupFailed(logger, ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>();
        var now = time.GetUtcNow().UtcDateTime;

        await db.UserSessions.Where(s => s.ExpiresAtUtc <= now).ExecuteDeleteAsync(ct);
        var counterCutoff = now.AddDays(-1);
        await db.RateLimitCounters.Where(c => c.WindowStartUtc < counterCutoff).ExecuteDeleteAsync(ct);

        // Süresi dolmuş bekleyen e-postalar gönderilmez; içerik ve alıcı silinir.
        await db.OutboxEmails
            .Where(o => o.Status == OutboxStatus.Pending && o.ExpiresAtUtc <= now)
            .ExecuteUpdateAsync(u => u
                .SetProperty(o => o.Status, OutboxStatus.Expired)
                .SetProperty(o => o.CompletedAtUtc, now)
                .SetProperty(o => o.ProtectedBody, (byte[]?)null)
                .SetProperty(o => o.Recipient, (string?)null)
                .SetProperty(o => o.Subject, (string?)null)
                .SetProperty(o => o.LastError, "Expired"), ct);

        var outboxCutoff = now.AddDays(-outboxOptions.Value.RetentionDays);
        await db.OutboxEmails.Where(o => o.CompletedAtUtc != null && o.CompletedAtUtc < outboxCutoff).ExecuteDeleteAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Periyodik temizlik başarısız ({ErrorType}).")]
    private static partial void LogCleanupFailed(ILogger logger, string errorType);
}
