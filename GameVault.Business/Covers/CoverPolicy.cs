using GameVault.Models.Library;
using Microsoft.Extensions.Options;

namespace GameVault.Business.Covers;

public sealed class CoverOptions
{
    public const string SectionName = "Covers";

    /// <summary>
    /// Harici kapak görsellerine izin verilen alan adları (tam eşleşme, yalnızca https).
    /// Sunucu bu adresleri hiçbir zaman indirmez; görseli tarayıcı doğrudan yükler ve
    /// aynı liste Content-Security-Policy img-src yönergesine eklenir.
    /// </summary>
    public List<string> AllowedHosts { get; set; } = [];
}

public sealed record BuiltInCover(string Key, string Name)
{
    public string Path => $"/covers/{Key}.svg";
}

public sealed record CoverResolution(bool Succeeded, string? Value, string? Error);

/// <summary>
/// Kapak görseli adresleri için tek doğrulama noktası. Hem kaydederken hem de
/// gösterirken kullanılır; böylece eski ya da elle değiştirilmiş veriler de
/// izin listesi dışında bir adresi tarayıcıya yükletemez.
/// </summary>
public sealed class CoverPolicy
{
    public static readonly IReadOnlyList<BuiltInCover> BuiltIn =
    [
        new("nebula", "Nebula"),
        new("neon-grid", "Neon ızgara"),
        new("forest", "Orman"),
        new("dungeon", "Zindan"),
        new("racer", "Pist"),
        new("ocean", "Okyanus"),
        new("desert", "Çöl"),
        new("circuit", "Devre")
    ];

    private readonly HashSet<string> _allowedHosts;

    public CoverPolicy(IOptions<CoverOptions> options)
    {
        _allowedHosts = new HashSet<string>(
            options.Value.AllowedHosts.Select(h => h.Trim().ToLowerInvariant()).Where(h => h.Length > 0),
            StringComparer.Ordinal);
    }

    public IReadOnlyCollection<string> AllowedHosts => _allowedHosts;

    public CoverResolution Resolve(string? choice, string? url)
    {
        choice = string.IsNullOrWhiteSpace(choice) ? CoverChoices.None : choice.Trim();

        if (choice == CoverChoices.None)
        {
            return new(true, null, null);
        }

        if (choice.StartsWith(CoverChoices.BuiltInPrefix, StringComparison.Ordinal))
        {
            var key = choice[CoverChoices.BuiltInPrefix.Length..];
            var cover = BuiltIn.FirstOrDefault(c => c.Key == key);
            return cover is null
                ? new(false, null, "Geçersiz hazır kapak seçimi.")
                : new(true, cover.Path, null);
        }

        if (choice == CoverChoices.Url)
        {
            var trimmed = url?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return new(false, null, "Görsel adresini girin ya da başka bir kapak seçin.");
            }

            if (_allowedHosts.Count == 0)
            {
                return new(false, null, "Bu kurulumda harici kapak adreslerine izin verilmiyor; hazır kapaklardan birini seçin.");
            }

            return IsAllowedExternal(trimmed)
                ? new(true, trimmed, null)
                : new(false, null, $"Yalnızca şu alan adlarından https görselleri kullanılabilir: {string.Join(", ", _allowedHosts)}.");
        }

        return new(false, null, "Geçersiz kapak seçimi.");
    }

    /// <summary>Kayıtlı değer gösterilebilir mi? (hazır kapak ya da izinli https adresi)</summary>
    public bool IsDisplayable(string? stored) =>
        stored is not null && (BuiltIn.Any(c => c.Path == stored) || IsAllowedExternal(stored));

    /// <summary>Düzenleme formu için kayıtlı değerden seçim üretir.</summary>
    public static (string Choice, string? Url) ToChoice(string? stored)
    {
        if (string.IsNullOrEmpty(stored))
        {
            return (CoverChoices.None, null);
        }

        var builtIn = BuiltIn.FirstOrDefault(c => c.Path == stored);
        return builtIn is not null
            ? (CoverChoices.BuiltInPrefix + builtIn.Key, null)
            : (CoverChoices.Url, stored);
    }

    private bool IsAllowedExternal(string value)
    {
        if (value.Length > 500 || value.Any(char.IsControl))
        {
            return false;
        }

        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(uri.UserInfo)
            && uri.IsDefaultPort
            && string.IsNullOrEmpty(uri.Fragment)
            && _allowedHosts.Contains(uri.IdnHost.ToLowerInvariant());
    }
}
