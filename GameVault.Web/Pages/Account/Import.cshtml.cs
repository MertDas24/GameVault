using GameVault.Business.Account;
using GameVault.Business.Security;
using GameVault.Models.Account;
using GameVault.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace GameVault.Web.Pages.Account;

/// <summary>
/// Dışa aktarma dosyasından kütüphaneye oyun ekler. Uygulamanın genel istek sınırı (128 KB) yalnızca bu sayfa için
/// dosya boyutu kadar yükseltilir; dosya diske yazılmaz, doğrudan akıştan okunur.
/// </summary>
[RequestSizeLimit(MaxRequestBytes)]
[RequestFormLimits(MultipartBodyLengthLimit = MaxRequestBytes)]
public sealed class ImportModel(ImportService imports, DbRateLimiter limiter) : AppPageModel
{
    /// <summary>Dosya + form alanları + multipart sınırları için küçük bir pay.</summary>
    private const int MaxRequestBytes = ImportService.MaxFileBytes + 64 * 1024;

    public const int MaxListedIssues = 50;

    public ImportResult? Result { get; private set; }

    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(IFormFile? file, string? mode, CancellationToken ct)
    {
        var dryRun = !string.Equals(mode, "import", StringComparison.Ordinal);

        if (file is null || file.Length == 0)
        {
            Error = "Bir JSON dosyası seç.";
            return PageWithStatus(StatusCodes.Status400BadRequest);
        }

        if (file.Length > ImportService.MaxFileBytes)
        {
            Error = $"Dosya en fazla {ImportService.MaxFileBytes / (1024 * 1024)} MB olabilir.";
            return PageWithStatus(StatusCodes.Status400BadRequest);
        }

        if (!await limiter.TryAcquireAsync("import-user", limiter.Options.ImportPerUser, ClientPartition.ForUser(CurrentUserId), ct))
        {
            Error = "Kısa sürede çok fazla içe aktarma denemesi yapıldı. Lütfen daha sonra tekrar dene.";
            return PageWithStatus(StatusCodes.Status429TooManyRequests);
        }

        await using var stream = file.OpenReadStream();
        var result = await imports.ImportAsync(CurrentUserId, stream, dryRun, ct);
        if (!result.Succeeded)
        {
            Error = result.Errors.Values.First();
            return PageWithStatus(StatusCodes.Status400BadRequest);
        }

        Result = result.Value;
        return Page();
    }
}
