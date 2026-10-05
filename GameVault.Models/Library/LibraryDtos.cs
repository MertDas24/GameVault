using GameVault.Entities;

namespace GameVault.Models.Library;

public sealed record GameCard(
    Guid Id,
    string Title,
    string? CoverImageUrl,
    string PlatformName,
    GameStatus Status,
    int? Rating,
    int PlaytimeHours,
    bool IsFavorite,
    DateTime DateAdded);

public sealed record GameDetails(
    Guid Id,
    string Title,
    string? Description,
    string? CoverImageUrl,
    int PlatformId,
    string PlatformName,
    IReadOnlyList<ReferenceItem> Genres,
    GameStatus Status,
    int? Rating,
    int PlaytimeHours,
    bool IsFavorite,
    string? Notes,
    DateOnly? StartDate,
    DateOnly? CompletionDate,
    DateTime DateAdded,
    DateTime UpdatedAtUtc,
    DateOnly? ReleaseDate = null,
    int? RawgId = null);

public sealed record ReferenceItem(int Id, string Name);

/// <summary>Kullanıcının kütüphanesinde aynı adı taşıyan bir oyun (ekleme öncesi uyarı için).</summary>
public sealed record SameTitleGame(Guid Id, string Title, string PlatformName, int? RawgId);

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => TotalCount == 0 ? 1 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < TotalPages;
}
