using System.Security.Cryptography;
using System.Text;
using GameVault.DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GameVault.Business.Security;

public sealed class RateLimitRule
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }

    public RateLimitRule()
    {
    }

    public RateLimitRule(int permitLimit, TimeSpan window)
    {
        PermitLimit = permitLimit;
        WindowSeconds = (int)window.TotalSeconds;
    }
}

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public bool Enabled { get; set; } = true;

    public RateLimitRule LoginPerIp { get; set; } = new(30, TimeSpan.FromMinutes(15));
    public RateLimitRule LoginPerAccount { get; set; } = new(10, TimeSpan.FromMinutes(15));
    public RateLimitRule RegisterPerIp { get; set; } = new(10, TimeSpan.FromHours(1));
    public RateLimitRule ForgotPasswordPerIp { get; set; } = new(10, TimeSpan.FromMinutes(15));
    public RateLimitRule ForgotPasswordPerAccount { get; set; } = new(3, TimeSpan.FromHours(1));
    public RateLimitRule ResetPasswordPerIp { get; set; } = new(10, TimeSpan.FromMinutes(15));
    public RateLimitRule ConfirmationPerIp { get; set; } = new(10, TimeSpan.FromMinutes(15));
    public RateLimitRule ConfirmationPerAccount { get; set; } = new(3, TimeSpan.FromHours(1));
    public RateLimitRule SensitivePerUser { get; set; } = new(10, TimeSpan.FromMinutes(15));
    public RateLimitRule ExportPerUser { get; set; } = new(10, TimeSpan.FromHours(1));
}

/// <summary>
/// Veritabanı tabanlı sabit pencereli oran sınırlayıcı. Sayaç artırma tek bir atomik
/// MERGE ifadesiyle yapılır; tüm uygulama örnekleri aynı sayaçları paylaşır.
/// Bölüm anahtarları (IP, e-posta) SHA-256 ile özetlenerek saklanır.
/// </summary>
public sealed class DbRateLimiter(GameVaultDbContext db, IOptions<RateLimitOptions> options, TimeProvider time)
{
    public RateLimitOptions Options => options.Value;

    /// <returns>İzin verildiyse true; sınır aşıldıysa false.</returns>
    public async Task<bool> TryAcquireAsync(string policy, RateLimitRule rule, string partition, CancellationToken ct = default)
    {
        if (!options.Value.Enabled || rule.PermitLimit <= 0 || rule.WindowSeconds <= 0)
        {
            return true;
        }

        var key = BuildKey(policy, partition);
        var now = time.GetUtcNow().UtcDateTime;
        var windowTicks = TimeSpan.FromSeconds(rule.WindowSeconds).Ticks;
        var windowStart = new DateTime(now.Ticks - (now.Ticks % windowTicks), DateTimeKind.Utc);

        var counts = await db.Database.SqlQuery<int>($"""
            MERGE [RateLimitCounters] WITH (HOLDLOCK) AS t
            USING (SELECT {key} AS [Key]) AS s ON t.[Key] = s.[Key]
            WHEN MATCHED THEN UPDATE SET
                [Count] = CASE WHEN t.[WindowStartUtc] = {windowStart} THEN t.[Count] + 1 ELSE 1 END,
                [WindowStartUtc] = {windowStart}
            WHEN NOT MATCHED THEN INSERT ([Key], [WindowStartUtc], [Count]) VALUES ({key}, {windowStart}, 1)
            OUTPUT inserted.[Count] AS [Value];
            """).ToListAsync(ct);

        return counts.Count == 1 && counts[0] <= rule.PermitLimit;
    }

    internal static string BuildKey(string policy, string partition)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(partition));
        return $"{policy}:{Convert.ToHexStringLower(hash)[..40]}";
    }
}
