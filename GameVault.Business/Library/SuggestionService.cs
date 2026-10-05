using GameVault.DataAccess;
using GameVault.Entities;
using GameVault.Models.Library;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Business.Library;

/// <summary>
/// "Ne oynasam?": kullanıcının sırada bekleyen oyunları arasından filtrelere uyan rastgele bir oyun seçer.
/// Seçim veritabanında yapılır (sayım + rastgele konum); tüm kütüphane belleğe yüklenmez.
/// Her sorgu çağıranın kimliğiyle sınırlıdır.
/// </summary>
public sealed class SuggestionService(GameVaultDbContext db)
{
    public async Task<SuggestionResult> SuggestAsync(Guid userId, SuggestionFilter filter, Guid? excludeId = null, CancellationToken ct = default)
    {
        var statuses = filter.Statuses.ToList();
        var backlog = db.LibraryGames.AsNoTracking().Where(g => g.UserId == userId && statuses.Contains(g.Status));
        var backlogCount = await backlog.CountAsync(ct);
        if (backlogCount == 0)
        {
            return new SuggestionResult(null, 0, 0, 0);
        }

        var matching = backlog;
        if (filter.PlatformId is { } platformId)
        {
            matching = matching.Where(g => g.PlatformId == platformId);
        }

        if (filter.GenreId is { } genreId)
        {
            matching = matching.Where(g => g.Genres.Any(x => x.Id == genreId));
        }

        var unknownEstimate = 0;
        if (filter.MaxHours is { } maxHours)
        {
            // Tahmini süresi girilmemiş oyunlar süre filtresine uyup uymadığı bilinemediği için elenir ve ayrıca sayılır.
            unknownEstimate = await matching.CountAsync(g => g.EstimatedHours == null, ct);
            matching = matching.Where(g => g.EstimatedHours != null && g.EstimatedHours <= maxHours);
        }

        var matchCount = await matching.CountAsync(ct);
        if (matchCount == 0)
        {
            return new SuggestionResult(null, backlogCount, 0, unknownEstimate);
        }

        // "Başka öner": birden fazla seçenek varsa az önce önerilen oyun tekrar gelmez.
        var candidates = matching;
        var candidateCount = matchCount;
        if (excludeId is { } excluded && matchCount > 1 && await matching.AnyAsync(g => g.Id == excluded, ct))
        {
            candidates = matching.Where(g => g.Id != excluded);
            candidateCount = matchCount - 1;
        }

        var ordered = candidates.OrderBy(g => g.Id);
        var offset = Random.Shared.Next(candidateCount);
        // Sayım ile seçim arasında oyun silinmişse (çok nadir) ilk aday alınır.
        var game = await Project(ordered.Skip(offset)).FirstOrDefaultAsync(ct)
            ?? await Project(ordered).FirstOrDefaultAsync(ct);

        return new SuggestionResult(game, backlogCount, matchCount, unknownEstimate);
    }

    private static IQueryable<GameSuggestion> Project(IQueryable<LibraryGame> games) =>
        games.Select(g => new GameSuggestion(
            g.Id,
            g.Title,
            g.CoverImageUrl,
            g.Platform.Name,
            g.Genres.OrderBy(x => x.Name).Select(x => x.Name).ToList(),
            g.Status,
            g.EstimatedHours));
}
