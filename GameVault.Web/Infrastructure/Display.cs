using System.Globalization;
using System.Security.Claims;

namespace GameVault.Web.Infrastructure;

/// <summary>Görüntüleme biçimlendirmeleri (tr-TR, yapılandırılmış saat dilimi).</summary>
public sealed class Display
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private readonly TimeZoneInfo _zone;

    public Display(Microsoft.Extensions.Options.IOptions<AppOptions> options)
    {
        _zone = TimeZoneInfo.FindSystemTimeZoneById(options.Value.DisplayTimeZone);
    }

    public string Date(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone).ToString("d MMMM yyyy", Turkish);

    public string Timestamp(DateTime utc) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), _zone).ToString("d MMMM yyyy HH:mm", Turkish);

    public static string Date(DateOnly date) => date.ToString("d MMMM yyyy", Turkish);

    public static string Number(long value) => value.ToString("N0", Turkish);

    public static string Percent(double ratio) => ratio.ToString("P0", Turkish);

    public static string OneDecimal(double value) => value.ToString("0.0", Turkish);

    public static string Initials(string title)
    {
        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var letters = words.Where(w => char.IsLetterOrDigit(w[0])).Take(2).Select(w => char.ToUpper(w[0], Turkish));
        var initials = string.Concat(letters);
        return initials.Length > 0 ? initials : "?";
    }

    public static string UserLabel(ClaimsPrincipal user) =>
        user.FindFirstValue(AppClaimsPrincipalFactory.DisplayNameClaim)
        ?? user.FindFirstValue(ClaimTypes.Email)
        ?? user.Identity?.Name
        ?? "Hesabım";
}
