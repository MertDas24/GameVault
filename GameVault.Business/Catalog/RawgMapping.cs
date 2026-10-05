using System.Globalization;
using System.Text.RegularExpressions;
using GameVault.Models.Catalog;
using GameVault.Models.Library;

namespace GameVault.Business.Catalog;

/// <summary>
/// RAWG verisini doğrular ve GameVault modellerine dönüştürür. Harici veri güvenilmez kabul edilir:
/// uzunluklar kırpılır, tarih ve görsel adresi sıkı biçimde ayrıştırılır, beklenmeyen değerler atılır.
/// </summary>
public static partial class RawgMapping
{
    public const int MaxTags = 10;

    // RAWG platform/tür kısa adı → GameVault başvuru verisindeki ad. Listede olmayanlar eşlenmez;
    // kullanıcı formda kendisi seçer. Platform öncelik sırası bu listenin sırasıdır.
    private static readonly (string Slug, string Platform)[] PlatformMap =
    [
        ("pc", "PC"),
        ("playstation5", "PlayStation 5"),
        ("playstation4", "PlayStation 4"),
        ("xbox-series-x", "Xbox Series X|S"),
        ("xbox-one", "Xbox One"),
        ("nintendo-switch", "Nintendo Switch"),
        ("ios", "Mobil"),
        ("android", "Mobil")
    ];

    private static readonly Dictionary<string, string> GenreMap = new(StringComparer.Ordinal)
    {
        ["action"] = "Aksiyon",
        ["adventure"] = "Macera",
        ["role-playing-games-rpg"] = "Rol Yapma (RPG)",
        ["strategy"] = "Strateji",
        ["shooter"] = "Nişancı",
        ["racing"] = "Yarış",
        ["sports"] = "Spor",
        ["simulation"] = "Simülasyon",
        ["puzzle"] = "Bulmaca",
        ["platformer"] = "Platform",
        ["fighting"] = "Dövüş"
    };

    /// <summary>RAWG oyununu doğrulanmış katalog modeline dönüştürür; kullanılamaz kayıtlar için null döner.</summary>
    internal static CatalogGame? ToCatalogGame(RawgGame? game)
    {
        if (game is null || game.Id <= 0)
        {
            return null;
        }

        var name = CleanText(game.Name, GameInput.TitleMaxLength);
        if (name is null)
        {
            return null;
        }

        return new CatalogGame(
            game.Id,
            name,
            SafeCoverUrl(game.BackgroundImage),
            ParseDate(game.Released),
            CleanTags(game.Platforms?.Select(p => p.Platform)),
            CleanTags(game.Genres),
            // RAWG ortalama oynanma süresi; 0 "bilinmiyor" demektir.
            game.Playtime is >= 1 and <= GameInput.MaxEstimatedHours ? game.Playtime : null);
    }

    /// <summary>
    /// Seçilen katalog oyunundan "Oyun ekle" formunu doldurur. Kaydetmeden önce kullanıcı bilgileri
    /// görür ve değiştirebilir; kayıt yine normal doğrulamadan geçer.
    /// </summary>
    public static GameInput ToGameInput(CatalogGame game, IReadOnlyList<ReferenceItem> platforms, IReadOnlyList<ReferenceItem> genres)
    {
        var platformIds = platforms.ToDictionary(p => p.Name, p => p.Id, StringComparer.Ordinal);
        var genreIds = genres.ToDictionary(g => g.Name, g => g.Id, StringComparer.Ordinal);
        var slugs = game.Platforms.Select(p => p.Slug).ToHashSet(StringComparer.Ordinal);

        int? platformId = null;
        foreach (var (slug, name) in PlatformMap)
        {
            if (slugs.Contains(slug) && platformIds.TryGetValue(name, out var id))
            {
                platformId = id;
                break;
            }
        }

        var mappedGenres = game.Genres
            .Select(g => GenreMap.TryGetValue(g.Slug, out var name) && genreIds.TryGetValue(name, out var id) ? id : (int?)null)
            .OfType<int>()
            .Distinct()
            .Take(GameInput.MaxGenres)
            .ToList();

        return new GameInput
        {
            Title = game.Name,
            RawgId = game.RawgId,
            ReleaseDate = game.Released,
            EstimatedHours = game.EstimatedHours,
            PlatformId = platformId,
            GenreIds = mappedGenres,
            CoverChoice = game.CoverUrl is null ? CoverChoices.None : CoverChoices.Url,
            CoverUrl = game.CoverUrl
        };
    }

    /// <summary>
    /// Yalnızca RAWG'ın görsel sunucusundaki https adreslerini kabul eder. Bant genişliğini azaltmak için
    /// RAWG'ın hazır kırpılmış (600x400) sürümü kullanılır.
    /// </summary>
    internal static string? SafeCoverUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 1000
            || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.Equals(uri.IdnHost, RawgOptions.CoverHost, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var path = uri.AbsolutePath;
        const string fullSize = "/media/games/";
        if (path.StartsWith(fullSize, StringComparison.Ordinal))
        {
            path = "/media/crop/600/400/games/" + path[fullSize.Length..];
        }

        var result = $"https://{RawgOptions.CoverHost}{path}";
        return result.Length <= GameInput.CoverUrlMaxLength ? result : null;
    }

    internal static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static string? CleanText(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = new string(value.Where(c => !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length == 0)
        {
            return null;
        }

        return cleaned.Length <= maxLength ? cleaned : cleaned[..maxLength].TrimEnd();
    }

    private static List<CatalogTag> CleanTags(IEnumerable<RawgNamed?>? tags) =>
        (tags ?? [])
            .Where(t => t?.Slug is not null && SlugPattern().IsMatch(t.Slug))
            .Select(t => new CatalogTag(t!.Slug!, CleanText(t.Name, 60) ?? t.Slug!))
            .DistinctBy(t => t.Slug)
            .Take(MaxTags)
            .ToList();

    [GeneratedRegex("^[a-z0-9-]{1,50}$")]
    private static partial Regex SlugPattern();
}
