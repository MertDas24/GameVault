using System.Globalization;
using GameVault.Entities;

namespace GameVault.Models.Library;

public enum LibrarySort
{
    AddedDesc,
    AddedAsc,
    TitleAsc,
    TitleDesc,
    RatingDesc,
    PlaytimeDesc,
    UpdatedDesc
}

/// <summary>
/// Kütüphane listesi için doğrulanmış, sınırlandırılmış sorgu. Geçersiz ya da aralık dışı
/// girdiler hata üretmek yerine güvenli varsayılanlara indirgenir.
/// </summary>
public sealed record LibraryQuery
{
    public const int MaxSearchLength = 100;
    public const int MaxPage = 10_000;
    public const int DefaultPageSize = 24;
    public static readonly IReadOnlyList<int> AllowedPageSizes = [12, 24, 48];

    public static readonly IReadOnlyDictionary<string, LibrarySort> SortKeys = new Dictionary<string, LibrarySort>(StringComparer.Ordinal)
    {
        ["added_desc"] = LibrarySort.AddedDesc,
        ["added_asc"] = LibrarySort.AddedAsc,
        ["title_asc"] = LibrarySort.TitleAsc,
        ["title_desc"] = LibrarySort.TitleDesc,
        ["rating_desc"] = LibrarySort.RatingDesc,
        ["playtime_desc"] = LibrarySort.PlaytimeDesc,
        ["updated_desc"] = LibrarySort.UpdatedDesc
    };

    public string? Search { get; init; }
    public GameStatus? Status { get; init; }
    public int? PlatformId { get; init; }
    public int? GenreId { get; init; }
    public int? CollectionId { get; init; }
    public bool FavoritesOnly { get; init; }
    public LibrarySort Sort { get; init; } = LibrarySort.AddedDesc;
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = DefaultPageSize;

    public string SortKey => SortKeys.First(kv => kv.Value == Sort).Key;

    public bool HasFilters =>
        Search is not null || Status is not null || PlatformId is not null || GenreId is not null || CollectionId is not null || FavoritesOnly;

    public static LibraryQuery FromRaw(
        string? search, string? status, string? platform, string? genre,
        string? favorites, string? sort, string? page, string? pageSize, string? collection = null)
    {
        var trimmed = search?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            trimmed = null;
        }
        else if (trimmed.Length > MaxSearchLength)
        {
            trimmed = trimmed[..MaxSearchLength];
        }

        return new LibraryQuery
        {
            Search = trimmed,
            Status = StatusLabels.FromKey(status),
            PlatformId = ParsePositive(platform),
            GenreId = ParsePositive(genre),
            CollectionId = ParsePositive(collection),
            FavoritesOnly = favorites is "1" or "true" or "on",
            Sort = sort is not null && SortKeys.TryGetValue(sort, out var s) ? s : LibrarySort.AddedDesc,
            Page = Math.Clamp(ParsePositive(page) ?? 1, 1, MaxPage),
            PageSize = ParsePositive(pageSize) is { } size && AllowedPageSizes.Contains(size) ? size : DefaultPageSize
        };
    }

    private static int? ParsePositive(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : null;
}
