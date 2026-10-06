using System.ComponentModel.DataAnnotations;
using System.Text;
using GameVault.DataAccess;
using GameVault.Entities;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GameVault.Business.Email;

public sealed class EmailOutboxOptions
{
    public const string SectionName = "EmailOutbox";

    /// <summary>Bir e-posta için toplam gönderim denemesi.</summary>
    [Range(1, 20)]
    public int MaxAttempts { get; set; } = 6;

    /// <summary>İlk yeniden deneme beklemesi; her denemede ikiye katlanır.</summary>
    [Range(0, 3600)]
    public int BaseDelaySeconds { get; set; } = 30;

    [Range(1, 86400)]
    public int MaxDelaySeconds { get; set; } = 1800;

    /// <summary>Yeni iş sinyali gelmezse kuyruğun kontrol aralığı.</summary>
    [Range(1, 300)]
    public int PollSeconds { get; set; } = 5;

    /// <summary>Bir işçinin bir işi sahiplendikten sonra diğer instance'lardan gizlediği süre.</summary>
    [Range(10, 3600)]
    public int LockSeconds { get; set; } = 120;

    [Range(1, 100)]
    public int BatchSize { get; set; } = 10;

    /// <summary>Tamamlanan/başarısız işlerin (gövdesiz) takip için tutulduğu süre.</summary>
    [Range(1, 365)]
    public int RetentionDays { get; set; } = 7;
}

/// <summary>Kuyruğa yeni iş eklendiğini aynı süreçteki işçiye bildirir (beklemeyi kısaltır).</summary>
public sealed class OutboxSignal : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);

    public void Dispose() => _signal.Dispose();
}

public static class OutboxKinds
{
    public const string ConfirmEmail = "confirm-email";
    public const string AccountExists = "account-exists";
    public const string ResetPassword = "reset-password";
    public const string TwoFactorChanged = "two-factor-changed";
}

/// <summary>E-postaları kalıcı kuyruğa yazar.</summary>
public sealed class EmailOutbox(
    GameVaultDbContext db,
    IDataProtectionProvider dataProtection,
    OutboxSignal signal,
    TimeProvider time)
{
    internal const string ProtectorPurpose = "GameVault.EmailOutbox.Body.v1";

    /// <returns>Kuyruğa eklendiyse true; aynı DedupKey ile bekleyen bir iş varsa false.</returns>
    public async Task<bool> EnqueueAsync(
        EmailMessage message, string kind, Guid? userId, TimeSpan validFor, string? dedupKey = null, CancellationToken ct = default)
    {
        var protector = dataProtection.CreateProtector(ProtectorPurpose);
        var now = time.GetUtcNow().UtcDateTime;
        var entry = new OutboxEmail
        {
            Id = Guid.NewGuid(),
            Kind = kind,
            UserId = userId,
            Recipient = message.To,
            Subject = message.Subject,
            ProtectedBody = protector.Protect(Encoding.UTF8.GetBytes(message.TextBody)),
            DedupKey = dedupKey,
            Status = OutboxStatus.Pending,
            CreatedAtUtc = now,
            NextAttemptAtUtc = now,
            ExpiresAtUtc = now.Add(validFor)
        };

        db.OutboxEmails.Add(entry);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            db.Entry(entry).State = EntityState.Detached;
            return false;
        }

        signal.Notify();
        return true;
    }

    /// <summary>Tekrar anahtarı için zaman kovası (ör. aynı kullanıcıya 5 dakikada bir).</summary>
    public string Bucket(TimeSpan window) =>
        (time.GetUtcNow().UtcDateTime.Ticks / window.Ticks).ToString(System.Globalization.CultureInfo.InvariantCulture);
}
