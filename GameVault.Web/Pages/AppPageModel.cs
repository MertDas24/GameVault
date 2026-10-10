using System.Security.Claims;
using GameVault.Models.Library;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameVault.Web.Pages;

/// <summary>Oturum açmış kullanıcı sayfaları için ortak yardımcılar.</summary>
public abstract class AppPageModel : PageModel
{
    /// <summary>
    /// Geçerli kullanıcının kimliği. Yalnızca sunucu tarafı oturumdan okunur;
    /// istemciden gelen hiçbir userId değerine güvenilmez.
    /// </summary>
    protected Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Kimliği doğrulanmış kullanıcı bekleniyordu."));

    protected void Flash(string message) => TempData["Flash"] = message;

    protected void FlashError(string message) => TempData["FlashError"] = message;

    /// <summary>Servis hatalarını ModelState'e taşır (alan adları verilen önekle).</summary>
    protected void AddErrors(IReadOnlyDictionary<string, string> errors, string prefix)
    {
        foreach (var (field, message) in errors)
        {
            ModelState.AddModelError(string.IsNullOrEmpty(field) ? string.Empty : $"{prefix}.{field}", message);
        }
    }

    /// <summary>Açık yönlendirmeye karşı yalnızca uygulama içi yerel adreslere izin verir.</summary>
    protected string SafeLocalUrl(string? url, string fallback) =>
        !string.IsNullOrEmpty(url) && Url.IsLocalUrl(url) ? url : fallback;

    protected PageResult PageWithStatus(int statusCode)
    {
        var result = Page();
        result.StatusCode = statusCode;
        return result;
    }
}

public sealed record GameCardView(GameCard Game, string ReturnUrl, IReadOnlyList<CollectionSummary>? Collections = null);
