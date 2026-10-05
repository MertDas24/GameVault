using System.ComponentModel.DataAnnotations;
using GameVault.Business.Covers;
using GameVault.DataAccess;
using GameVault.Entities;
using GameVault.Models.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GameVault.Business.Library;

public sealed class LibraryOptions
{
    public const string SectionName = "Library";

    /// <summary>Kullanıcı başına en fazla oyun sayısı; dışa aktarma ve sorgu boyutunu sınırlar.</summary>
    [Range(1, 1_000_000)]
    public int MaxGamesPerUser { get; set; } = 5000;
}

/// <summary>
/// Kütüphane işlemleri. Her sorgu çağıranın kimliğiyle (userId) filtrelenir; userId
/// yalnızca sunucu tarafındaki oturumdan gelir, istemci girdisinden asla alınmaz.
/// Başka bir kullanıcıya ait kayıtlar "bulunamadı" olarak döner (varlık sızdırılmaz).
/// </summary>
public sealed class LibraryService(
    GameVaultDbContext db,
    CoverPolicy covers,
    TimeProvider time,
    IOptions<LibraryOptions> options)
{
    private IQueryable<LibraryGame> OwnedBy(Guid userId) => db.LibraryGames.Where(g => g.UserId == userId);

    public async Task<PagedResult<GameCard>> SearchAsync(Guid userId, LibraryQuery query, CancellationToken ct = default)
    {
        var games = OwnedBy(userId).AsNoTracking();

        if (query.Search is { } search)
        {
            // EF Core parametreli LIKE üretir ve joker karakterleri kaçırır.
            games = games.Where(g => g.Title.Contains(search));
        }

        if (query.Status is { } status)
        {
            games = games.Where(g => g.Status == status);
        }

        if (query.PlatformId is { } platformId)
        {
            games = games.Where(g => g.PlatformId == platformId);
        }

        if (query.GenreId is { } genreId)
        {
            games = games.Where(g => g.Genres.Any(genre => genre.Id == genreId));
        }

        if (query.FavoritesOnly)
        {
            games = games.Where(g => g.IsFavorite);
        }

        var total = await games.CountAsync(ct);

        var ordered = query.Sort switch
        {
            LibrarySort.AddedAsc => games.OrderBy(g => g.DateAdded),
            LibrarySort.TitleAsc => games.OrderBy(g => g.Title),
            LibrarySort.TitleDesc => games.OrderByDescending(g => g.Title),
            LibrarySort.RatingDesc => games.OrderByDescending(g => g.Rating.HasValue).ThenByDescending(g => g.Rating),
            LibrarySort.PlaytimeDesc => games.OrderByDescending(g => g.PlaytimeHours),
            LibrarySort.UpdatedDesc => games.OrderByDescending(g => g.UpdatedAtUtc),
            _ => games.OrderByDescending(g => g.DateAdded)
        };

        // Sayfa aralık dışındaysa son sayfaya çek.
        var totalPages = Math.Max(1, (int)Math.Ceiling(total / (double)query.PageSize));
        var page = Math.Min(query.Page, totalPages);

        var items = await ordered
            .ThenBy(g => g.Id)
            .Skip((page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(g => new GameCard(
                g.Id, g.Title, g.CoverImageUrl, g.Platform.Name, g.Status,
                g.Rating, g.PlaytimeHours, g.IsFavorite, g.DateAdded))
            .ToListAsync(ct);

        return new PagedResult<GameCard>(items, page, query.PageSize, total);
    }

    public async Task<int> CountAsync(Guid userId, CancellationToken ct = default) =>
        await OwnedBy(userId).CountAsync(ct);

    public async Task<GameDetails?> GetAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var game = await OwnedBy(userId)
            .AsNoTracking()
            .Where(g => g.Id == id)
            .Select(g => new
            {
                g.Id,
                g.Title,
                g.Description,
                g.CoverImageUrl,
                g.PlatformId,
                PlatformName = g.Platform.Name,
                Genres = g.Genres.OrderBy(x => x.Name).Select(x => new ReferenceItem(x.Id, x.Name)).ToList(),
                g.Status,
                g.Rating,
                g.PlaytimeHours,
                g.IsFavorite,
                g.Notes,
                g.StartDate,
                g.CompletionDate,
                g.DateAdded,
                g.UpdatedAtUtc,
                g.ReleaseDate,
                g.EstimatedHours,
                g.RawgId
            })
            .FirstOrDefaultAsync(ct);

        return game is null
            ? null
            : new GameDetails(
                game.Id, game.Title, game.Description, game.CoverImageUrl, game.PlatformId, game.PlatformName,
                game.Genres, game.Status, game.Rating, game.PlaytimeHours, game.IsFavorite, game.Notes,
                game.StartDate, game.CompletionDate, game.DateAdded, game.UpdatedAtUtc, game.ReleaseDate, game.RawgId, game.EstimatedHours);
    }

    public async Task<GameInput?> GetInputAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var details = await GetAsync(userId, id, ct);
        if (details is null)
        {
            return null;
        }

        var (choice, url) = CoverPolicy.ToChoice(details.CoverImageUrl);
        return new GameInput
        {
            Title = details.Title,
            Description = details.Description,
            CoverChoice = choice,
            CoverUrl = url,
            PlatformId = details.PlatformId,
            GenreIds = details.Genres.Select(g => g.Id).ToList(),
            Status = details.Status,
            PlaytimeHours = details.PlaytimeHours,
            Rating = details.Rating,
            Notes = details.Notes,
            IsFavorite = details.IsFavorite,
            StartDate = details.StartDate,
            CompletionDate = details.CompletionDate,
            ReleaseDate = details.ReleaseDate,
            EstimatedHours = details.EstimatedHours
        };
    }

    /// <summary>
    /// Kullanıcının kütüphanesinde aynı adı taşıyan oyunlar. Karşılaştırma veritabanı harmanlamasına göre
    /// büyük/küçük harf duyarsızdır. Bu bir kural değil, uyarıdır: aynı oyun farklı platformlarda ayrı kayıt olabilir.
    /// </summary>
    public async Task<IReadOnlyList<SameTitleGame>> FindSameTitleAsync(Guid userId, string title, CancellationToken ct = default)
    {
        var trimmed = title.Trim();
        if (trimmed.Length == 0)
        {
            return [];
        }

        return await OwnedBy(userId)
            .AsNoTracking()
            .Where(g => g.Title == trimmed)
            .OrderBy(g => g.DateAdded)
            .Select(g => new SameTitleGame(g.Id, g.Title, g.Platform.Name, g.RawgId))
            .Take(5)
            .ToListAsync(ct);
    }

    /// <summary>Verilen RAWG kimliklerinden hangilerinin kullanıcının kütüphanesinde olduğunu döner (RAWG kimliği → oyun kimliği).</summary>
    public async Task<IReadOnlyDictionary<int, Guid>> GetOwnedRawgIdsAsync(Guid userId, IReadOnlyCollection<int> rawgIds, CancellationToken ct = default)
    {
        if (rawgIds.Count == 0)
        {
            return new Dictionary<int, Guid>();
        }

        return await OwnedBy(userId)
            .AsNoTracking()
            .Where(g => g.RawgId != null && rawgIds.Contains(g.RawgId.Value))
            .ToDictionaryAsync(g => g.RawgId!.Value, g => g.Id, ct);
    }

    public async Task<OperationResult<Guid>> CreateAsync(Guid userId, GameInput input, CancellationToken ct = default)
    {
        if (await OwnedBy(userId).CountAsync(ct) >= options.Value.MaxGamesPerUser)
        {
            return OperationResult<Guid>.Invalid(string.Empty,
                $"Kütüphanende en fazla {options.Value.MaxGamesPerUser} oyun bulunabilir.");
        }

        var validated = await ValidateAsync(input, ct);
        if (validated.Errors is not null)
        {
            return OperationResult<Guid>.Invalid(validated.Errors);
        }

        if (input.RawgId is { } rawgId && await OwnedBy(userId).AnyAsync(g => g.RawgId == rawgId, ct))
        {
            return OperationResult<Guid>.Invalid(string.Empty, DuplicateRawgGameError);
        }

        var now = time.GetUtcNow().UtcDateTime;
        var game = new LibraryGame
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DateAdded = now,
            // RAWG kimliği yalnızca oluştururken atanır; Apply (güncelleme) onu değiştirmez.
            RawgId = input.RawgId
        };
        Apply(game, input, validated, now);

        db.LibraryGames.Add(game);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (input.RawgId is not null && IsUniqueViolation(ex))
        {
            // Aynı anda gelen iki istekte yukarıdaki kontrol ikisini de geçirebilir; indeks ikincisini durdurur.
            return OperationResult<Guid>.Invalid(string.Empty, DuplicateRawgGameError);
        }

        return OperationResult<Guid>.Success(game.Id);
    }

    public const string DuplicateRawgGameError = "Bu oyun zaten kütüphanende.";

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 };

    public async Task<OperationResult<Guid>> UpdateAsync(Guid userId, Guid id, GameInput input, CancellationToken ct = default)
    {
        var game = await OwnedBy(userId)
            .Include(g => g.Genres)
            .FirstOrDefaultAsync(g => g.Id == id, ct);

        if (game is null)
        {
            return OperationResult<Guid>.Missing();
        }

        var validated = await ValidateAsync(input, ct);
        if (validated.Errors is not null)
        {
            return OperationResult<Guid>.Invalid(validated.Errors);
        }

        Apply(game, input, validated, time.GetUtcNow().UtcDateTime);
        await db.SaveChangesAsync(ct);
        return OperationResult<Guid>.Success(game.Id);
    }

    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken ct = default) =>
        await OwnedBy(userId).Where(g => g.Id == id).ExecuteDeleteAsync(ct) > 0;

    /// <summary>Favori durumunu değiştirir; kayıt yoksa ya da başkasına aitse null döner.</summary>
    public async Task<bool?> ToggleFavoriteAsync(Guid userId, Guid id, CancellationToken ct = default)
    {
        var game = await OwnedBy(userId).FirstOrDefaultAsync(g => g.Id == id, ct);
        if (game is null)
        {
            return null;
        }

        game.IsFavorite = !game.IsFavorite;
        game.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return game.IsFavorite;
    }

    /// <summary>
    /// Oyunu "Oynuyorum" olarak işaretler; başlama tarihi boşsa <paramref name="today"/> yazılır
    /// (kullanıcının saat dilimindeki tarih). Kayıt yoksa ya da başkasına aitse false döner.
    /// </summary>
    public async Task<bool> StartPlayingAsync(Guid userId, Guid id, DateOnly today, CancellationToken ct = default)
    {
        var game = await OwnedBy(userId).FirstOrDefaultAsync(g => g.Id == id, ct);
        if (game is null)
        {
            return false;
        }

        game.Status = GameStatus.Playing;
        game.StartDate ??= today;
        game.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static void Apply(LibraryGame game, GameInput input, ValidatedInput validated, DateTime now)
    {
        // Güncellenebilir alanlar burada açıkça listelenir; UserId, Id ve DateAdded değiştirilmez.
        game.Title = input.Title.Trim();
        game.Description = NullIfBlank(input.Description);
        game.CoverImageUrl = validated.CoverUrl;
        game.PlatformId = input.PlatformId!.Value;
        game.Status = input.Status!.Value;
        game.PlaytimeHours = input.PlaytimeHours;
        game.Rating = input.Rating;
        game.Notes = NullIfBlank(input.Notes);
        game.IsFavorite = input.IsFavorite;
        game.StartDate = input.StartDate;
        game.CompletionDate = input.CompletionDate;
        game.ReleaseDate = input.ReleaseDate;
        game.EstimatedHours = input.EstimatedHours;
        game.UpdatedAtUtc = now;

        game.Genres.Clear();
        foreach (var genre in validated.Genres)
        {
            game.Genres.Add(genre);
        }
    }

    private sealed record ValidatedInput(Dictionary<string, string>? Errors, string? CoverUrl, List<Genre> Genres);

    private async Task<ValidatedInput> ValidateAsync(GameInput input, CancellationToken ct)
    {
        var errors = new Dictionary<string, string>();

        // Sayfa katmanından bağımsız olarak da aynı kurallar uygulanır.
        var annotationResults = new List<ValidationResult>();
        Validator.TryValidateObject(input, new ValidationContext(input), annotationResults, validateAllProperties: true);
        foreach (var result in annotationResults)
        {
            var field = result.MemberNames.FirstOrDefault() ?? string.Empty;
            errors.TryAdd(field, result.ErrorMessage ?? "Geçersiz değer.");
        }

        if (string.IsNullOrWhiteSpace(input.Title))
        {
            errors.TryAdd(nameof(GameInput.Title), "Oyun adı zorunludur.");
        }

        var cover = covers.Resolve(input.CoverChoice, input.CoverUrl);
        if (!cover.Succeeded)
        {
            errors.TryAdd(input.CoverChoice == CoverChoices.Url ? nameof(GameInput.CoverUrl) : nameof(GameInput.CoverChoice), cover.Error!);
        }

        if (input.PlatformId is { } platformId && !await db.Platforms.AnyAsync(p => p.Id == platformId, ct))
        {
            errors.TryAdd(nameof(GameInput.PlatformId), "Geçersiz platform.");
        }

        var genreIds = input.GenreIds.Distinct().ToList();
        List<Genre> genres = [];
        if (genreIds.Count > GameInput.MaxGenres)
        {
            errors.TryAdd(nameof(GameInput.GenreIds), "En fazla 5 tür seçebilirsiniz.");
        }
        else if (genreIds.Count > 0)
        {
            genres = await db.Genres.Where(g => genreIds.Contains(g.Id)).ToListAsync(ct);
            if (genres.Count != genreIds.Count)
            {
                errors.TryAdd(nameof(GameInput.GenreIds), "Geçersiz tür seçimi.");
            }
        }

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var earliest = new DateOnly(1970, 1, 1);
        if (input.StartDate is { } start && (start < earliest || start > today.AddDays(1)))
        {
            errors.TryAdd(nameof(GameInput.StartDate), "Başlama tarihi 1970 ile bugün arasında olmalı.");
        }

        if (input.CompletionDate is { } end && (end < earliest || end > today.AddDays(1)))
        {
            errors.TryAdd(nameof(GameInput.CompletionDate), "Bitirme tarihi 1970 ile bugün arasında olmalı.");
        }

        // Çıkış tarihi gelecekte olabilir (henüz çıkmamış oyunlar), ama makul bir aralıkta.
        if (input.ReleaseDate is { } released && (released < new DateOnly(1950, 1, 1) || released > today.AddYears(10)))
        {
            errors.TryAdd(nameof(GameInput.ReleaseDate), "Çıkış tarihi 1950 ile önümüzdeki 10 yıl arasında olmalı.");
        }

        if (input.StartDate is { } s && input.CompletionDate is { } c && c < s)
        {
            errors.TryAdd(nameof(GameInput.CompletionDate), "Bitirme tarihi başlama tarihinden önce olamaz.");
        }

        return new ValidatedInput(errors.Count > 0 ? errors : null, cover.Value, genres);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
