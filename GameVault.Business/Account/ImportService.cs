using System.Text.Json;
using GameVault.Business.Covers;
using GameVault.Business.Library;
using GameVault.DataAccess;
using GameVault.Entities;
using GameVault.Models.Account;
using GameVault.Models.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GameVault.Business.Account;

/// <summary>
/// Uygulamanın kendi dışa aktarma dosyasını (<see cref="AccountService.ExportFormat"/>) okuyup kütüphaneye ekler.
/// Dosyadaki kimlikler, kullanıcı ve hesap bilgileri yok sayılır: oyunlar her zaman çağıran kullanıcıya eklenir.
/// Platform ve türler ada göre eşleştirilir; doğrulama, formla eklemeyle aynı kurallarla <see cref="LibraryService"/>'te yapılır.
/// </summary>
public sealed class ImportService(GameVaultDbContext db, LibraryService library, IOptions<LibraryOptions> options)
{
    /// <summary>Kabul edilen en büyük dosya (bayt). Sayfa katmanı istek gövdesini de bu değerle sınırlar.</summary>
    public const int MaxFileBytes = 4 * 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        MaxDepth = 16,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    public async Task<OperationResult<ImportResult>> ImportAsync(Guid userId, Stream json, bool dryRun, CancellationToken ct = default)
    {
        ImportFile? file;
        try
        {
            file = await JsonSerializer.DeserializeAsync<ImportFile>(json, JsonOptions, ct);
        }
        catch (JsonException)
        {
            return Fail("Dosya okunamadı: geçerli bir JSON dosyası değil.");
        }

        if (file is null || !string.Equals(file.Format, AccountService.ExportFormat, StringComparison.Ordinal))
        {
            return Fail($"Bu dosya bir Game Vault dışa aktarma dosyası değil (beklenen biçim: {AccountService.ExportFormat}).");
        }

        var elements = file.Games ?? [];
        if (elements.Count == 0)
        {
            return Fail("Dosyada hiç oyun yok.");
        }

        if (elements.Count > options.Value.MaxGamesPerUser)
        {
            return Fail($"Dosyada en fazla {options.Value.MaxGamesPerUser} oyun bulunabilir.");
        }

        var platforms = await db.Platforms.AsNoTracking().ToDictionaryAsync(p => p.Name, p => p.Id, StringComparer.OrdinalIgnoreCase, ct);
        var genres = await db.Genres.AsNoTracking().ToDictionaryAsync(g => g.Name, g => g.Id, StringComparer.OrdinalIgnoreCase, ct);

        var candidates = new List<ImportCandidate>();
        var errors = new List<ImportIssue>();
        for (var i = 0; i < elements.Count; i++)
        {
            var position = i + 1;
            ImportGame? game;
            try
            {
                game = elements[i].ValueKind == JsonValueKind.Object ? elements[i].Deserialize<ImportGame>(JsonOptions) : null;
            }
            catch (JsonException)
            {
                game = null;
            }

            if (game is null)
            {
                errors.Add(new ImportIssue(position, Untitled, "Kayıt okunamadı: alanlardan biri beklenen türde değil."));
                continue;
            }

            var (candidate, error) = ToCandidate(position, game, platforms, genres);
            if (error is not null)
            {
                errors.Add(error);
            }
            else
            {
                candidates.Add(candidate!);
            }
        }

        return await library.ImportAsync(userId, candidates, errors, dryRun, ct);
    }

    private const string Untitled = "(adsız)";

    private static OperationResult<ImportResult> Fail(string message) => OperationResult<ImportResult>.Invalid(string.Empty, message);

    private static (ImportCandidate? Candidate, ImportIssue? Error) ToCandidate(
        int position, ImportGame game, Dictionary<string, int> platforms, Dictionary<string, int> genres)
    {
        var title = string.IsNullOrWhiteSpace(game.Title) ? Untitled : game.Title.Trim();
        ImportIssue Error(string message) => new(position, title, message);

        if (string.IsNullOrWhiteSpace(game.Platform) || !platforms.TryGetValue(game.Platform.Trim(), out var platformId))
        {
            return (null, Error($"Bilinmeyen platform: “{game.Platform}”."));
        }

        var genreIds = new List<int>();
        foreach (var name in game.Genres ?? [])
        {
            if (string.IsNullOrWhiteSpace(name) || !genres.TryGetValue(name.Trim(), out var genreId))
            {
                return (null, Error($"Bilinmeyen tür: “{name}”."));
            }

            genreIds.Add(genreId);
        }

        GameStatus status = GameStatus.NotStarted;
        if (!string.IsNullOrWhiteSpace(game.Status))
        {
            if (StatusLabels.FromKey(game.Status.Trim()) is not { } parsed)
            {
                return (null, Error($"Bilinmeyen durum: “{game.Status}”."));
            }

            status = parsed;
        }

        var (coverChoice, coverUrl) = CoverPolicy.ToChoice(game.CoverImageUrl);
        var input = new GameInput
        {
            Title = game.Title ?? string.Empty,
            Description = game.Description,
            CoverChoice = coverChoice,
            CoverUrl = coverUrl,
            PlatformId = platformId,
            GenreIds = genreIds,
            Status = status,
            PlaytimeHours = game.PlaytimeHours ?? 0,
            Rating = game.Rating,
            Notes = game.Notes,
            IsFavorite = game.IsFavorite ?? false,
            StartDate = game.StartDate,
            CompletionDate = game.CompletionDate,
            ReleaseDate = game.ReleaseDate,
            EstimatedHours = game.EstimatedHours,
            RawgId = game.RawgId
        };

        return (new ImportCandidate(position, input, ToUtc(game.DateAddedUtc)), null);
    }

    private static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } v => v,
        { Kind: DateTimeKind.Local } v => v.ToUniversalTime(),
        var v => DateTime.SpecifyKind(v.Value, DateTimeKind.Utc)
    };

    /// <summary>Dosyanın yalnızca okunan kısmı; hesap bilgileri ve dışa aktarma zamanı bilinçli olarak okunmaz.</summary>
    private sealed class ImportFile
    {
        public string? Format { get; set; }
        public List<JsonElement>? Games { get; set; }
    }

    /// <summary>Bir oyun kaydı. Id ve UpdatedAtUtc okunmaz: yeni kayıt her zaman yeni kimlik alır.</summary>
    private sealed class ImportGame
    {
        public string? Title { get; set; }
        public string? Description { get; set; }
        public string? CoverImageUrl { get; set; }
        public string? Platform { get; set; }
        public List<string?>? Genres { get; set; }
        public string? Status { get; set; }
        public int? Rating { get; set; }
        public int? PlaytimeHours { get; set; }
        public bool? IsFavorite { get; set; }
        public string? Notes { get; set; }
        public DateOnly? StartDate { get; set; }
        public DateOnly? CompletionDate { get; set; }
        public DateOnly? ReleaseDate { get; set; }
        public DateTime? DateAddedUtc { get; set; }
        public int? RawgId { get; set; }
        public int? EstimatedHours { get; set; }
    }
}
