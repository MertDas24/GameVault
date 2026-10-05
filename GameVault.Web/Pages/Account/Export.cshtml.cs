using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameVault.Business.Account;
using GameVault.Business.Security;
using GameVault.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Account;

public sealed class ExportModel(AccountService accounts, DbRateLimiter limiter, TimeProvider time) : AppPageModel
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        // Türkçe karakterler okunur kalsın; HTML'e hassas karakterler yine kaçırılır.
        Encoder = JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.BasicLatin, System.Text.Unicode.UnicodeRanges.Latin1Supplement, System.Text.Unicode.UnicodeRanges.LatinExtendedA)
    };

    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken ct)
    {
        var userId = CurrentUserId;
        if (!await limiter.TryAcquireAsync("export-user", limiter.Options.ExportPerUser, ClientPartition.ForUser(userId), ct))
        {
            Error = "Kısa sürede çok fazla dışa aktarma yapıldı. Lütfen daha sonra tekrar dene.";
            return PageWithStatus(StatusCodes.Status429TooManyRequests);
        }

        var document = await accounts.ExportAsync(userId, ct);
        if (document is null)
        {
            return NotFound();
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        var fileName = $"gamevault-export-{time.GetUtcNow():yyyyMMdd-HHmm}.json";
        return File(bytes, "application/json; charset=utf-8", fileName);
    }
}
