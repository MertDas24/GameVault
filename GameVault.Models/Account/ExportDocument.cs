namespace GameVault.Models.Account;

/// <summary>Kullanıcının kendi verilerinin dışa aktarım biçimi (JSON).</summary>
public sealed record ExportDocument(
    string Format,
    DateTime ExportedAtUtc,
    ExportAccount Account,
    IReadOnlyList<ExportGame> Games);

public sealed record ExportAccount(string Email, string? DisplayName, DateTime CreatedAtUtc);

public sealed record ExportGame(
    Guid Id,
    string Title,
    string? Description,
    string? CoverImageUrl,
    string Platform,
    IReadOnlyList<string> Genres,
    string Status,
    int? Rating,
    int PlaytimeHours,
    bool IsFavorite,
    string? Notes,
    DateOnly? StartDate,
    DateOnly? CompletionDate,
    DateTime DateAddedUtc,
    DateTime UpdatedAtUtc,
    DateOnly? ReleaseDate = null,
    int? RawgId = null);
