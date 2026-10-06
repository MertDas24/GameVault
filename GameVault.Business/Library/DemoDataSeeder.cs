using GameVault.DataAccess;
using GameVault.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Business.Library;

/// <summary>
/// Yerel geliştirme için tamamen kurgusal demo verisi. Yalnızca Development ortamında,
/// açık bir komutla (seed-demo) çalıştırılır; uygulama başlangıcında otomatik çalışmaz.
/// </summary>
public sealed class DemoDataSeeder(GameVaultDbContext db, UserManager<ApplicationUser> users, TimeProvider time)
{
    public const string DemoEmail = "demo@gamevault.test";

    private static readonly (string Title, string Platform, string[] Genres, GameStatus Status, int Hours, int? Rating, bool Fav, string Cover, string? Description, int? Estimate)[] Games =
    [
        ("Yıldız Tozu Kronikleri", "PC", ["Rol Yapma (RPG)", "Macera"], GameStatus.Playing, 42, 9, true, "nebula", "Terk edilmiş bir uzay istasyonunda geçen, seçimlerin önemli olduğu kurgusal bir rol yapma oyunu.", 60),
        ("Neon Kaçış 2088", "PlayStation 5", ["Aksiyon", "Platform"], GameStatus.Completed, 18, 8, false, "neon-grid", "Hızlı tempolu, ritim tabanlı bir platform oyunu.", 12),
        ("Kadim Ormanın Bekçisi", "Nintendo Switch", ["Macera", "Bulmaca"], GameStatus.OnHold, 11, null, false, "forest", null, 25),
        ("Zindan Ustası: Sonsuz Katlar", "PC", ["Roguelike", "Strateji"], GameStatus.Playing, 67, 10, true, "dungeon", "Her girişte değişen katlar ve kalıcı yükseltmeler.", 40),
        ("Asfalt Fırtınası", "Xbox Series X|S", ["Yarış"], GameStatus.Dropped, 3, 4, false, "racer", null, 15),
        ("Derin Mavi Keşif", "PC", ["Simülasyon", "Hayatta Kalma"], GameStatus.NotStarted, 0, null, false, "ocean", "Okyanus tabanında üs kurma simülasyonu.", 30),
        ("Kum Saatinin Sırrı", "PlayStation 4", ["Bulmaca", "Macera"], GameStatus.Completed, 9, 7, false, "desert", null, 8),
        ("Devre Kırıcı", "PC", ["Strateji"], GameStatus.Wishlist, 0, null, false, "circuit", "Siber güvenlik temalı kurgusal bir sıra tabanlı strateji oyunu.", 20),
        ("Çok Uzun Adlı Bir Oyun: Unutulmuş Krallıkların Son Muhafızının Destansı ve Bitmek Bilmeyen Yolculuğu", "PC", ["Rol Yapma (RPG)"], GameStatus.NotStarted, 0, null, false, "", "Uzun başlıkların arayüzde düzgün göründüğünü denemek için.", null)
    ];

    public async Task<bool> SeedAsync(string demoPassword, CancellationToken ct = default)
    {
        if (await users.FindByEmailAsync(DemoEmail) is not null)
        {
            return false;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var user = new ApplicationUser
        {
            UserName = DemoEmail,
            Email = DemoEmail,
            DisplayName = "Demo Oyuncu",
            EmailConfirmed = true,
            CreatedAtUtc = now
        };

        var created = await users.CreateAsync(user, demoPassword);
        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                "Demo kullanıcısı oluşturulamadı: " + string.Join(" ", created.Errors.Select(e => e.Description)));
        }

        var platforms = await db.Platforms.ToDictionaryAsync(p => p.Name, ct);
        var genres = await db.Genres.ToDictionaryAsync(g => g.Name, ct);

        var today = DateOnly.FromDateTime(now);
        // Kurgusal koleksiyonlar: oyunların sırasına göre (0, 3 → uzun; 1, 6 → kısa) bağlanır.
        var longOnes = new GameCollection { UserId = user.Id, Name = "Uzun soluklu", CreatedAtUtc = now };
        var shortOnes = new GameCollection { UserId = user.Id, Name = "Kısa ve tatlı", CreatedAtUtc = now };
        var i = 0;
        foreach (var g in Games)
        {
            var added = now.AddDays(-(Games.Length - i) * 3);
            db.LibraryGames.Add(new LibraryGame
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Title = g.Title,
                Description = g.Description,
                CoverImageUrl = g.Cover.Length > 0 ? $"/covers/{g.Cover}.svg" : null,
                PlatformId = platforms[g.Platform].Id,
                Genres = g.Genres.Select(name => genres[name]).ToList(),
                Collections = i switch { 0 or 3 => [longOnes], 1 or 6 => [shortOnes], _ => [] },
                Status = g.Status,
                PlaytimeHours = g.Hours,
                Rating = g.Rating,
                IsFavorite = g.Fav,
                EstimatedHours = g.Estimate,
                // Kurgusal tarihler: istatistiklerdeki aylık etkinlik grafiği boş görünmesin diye son aylara yayılır.
                StartDate = g.Status is GameStatus.NotStarted or GameStatus.Wishlist ? null : today.AddMonths(-(i % 6 + 2)),
                CompletionDate = g.Status == GameStatus.Completed ? today.AddMonths(-(i % 3)) : null,
                DateAdded = added,
                UpdatedAtUtc = added
            });
            i++;
        }

        await db.SaveChangesAsync(ct);
        return true;
    }
}
