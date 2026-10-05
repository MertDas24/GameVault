using GameVault.Entities;

namespace GameVault.Models.Library;

/// <summary>"Ne oynasam?" filtreleri. Geçersiz değerler sorgu oluşturulmadan önce temizlenir.</summary>
public sealed record SuggestionFilter(int? PlatformId, int? GenreId, int? MaxHours, bool IncludeWishlist)
{
    public static readonly SuggestionFilter None = new(null, null, null, false);

    /// <summary>Kimlikler pozitif, süre 1–1000 aralığında olmalı; aksi hâlde filtre yok sayılır.</summary>
    public static SuggestionFilter Normalize(int? platformId, int? genreId, int? maxHours, bool includeWishlist) =>
        new(platformId is > 0 ? platformId : null,
            genreId is > 0 ? genreId : null,
            maxHours is >= 1 and <= GameInput.MaxEstimatedHours ? maxHours : null,
            includeWishlist);

    public bool HasFilters => PlatformId is not null || GenreId is not null || MaxHours is not null || IncludeWishlist;

    /// <summary>Öneri havuzu: başlanmamış ve ara verilmiş oyunlar, isteğe bağlı olarak istek listesi.</summary>
    public IReadOnlyList<GameStatus> Statuses => IncludeWishlist
        ? [GameStatus.NotStarted, GameStatus.OnHold, GameStatus.Wishlist]
        : [GameStatus.NotStarted, GameStatus.OnHold];
}

public sealed record GameSuggestion(
    Guid Id,
    string Title,
    string? CoverImageUrl,
    string PlatformName,
    IReadOnlyList<string> Genres,
    GameStatus Status,
    int? EstimatedHours);

/// <param name="Game">Önerilen oyun; uygun oyun yoksa null.</param>
/// <param name="BacklogCount">Filtreler uygulanmadan önce havuzdaki oyun sayısı.</param>
/// <param name="MatchCount">Filtrelere uyan oyun sayısı.</param>
/// <param name="UnknownEstimateCount">Süre filtresi yüzünden elenen, tahmini süresi girilmemiş oyun sayısı.</param>
public sealed record SuggestionResult(GameSuggestion? Game, int BacklogCount, int MatchCount, int UnknownEstimateCount);
