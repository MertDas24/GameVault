using GameVault.DataAccess;
using GameVault.Entities;
using GameVault.Models.Account;
using GameVault.Models.Library;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Business.Account;

public sealed class AccountService(GameVaultDbContext db, UserManager<ApplicationUser> users, TimeProvider time)
{
    public const string ExportFormat = "gamevault-export/v1";

    /// <summary>Yalnızca verilen kullanıcının verilerini içeren dışa aktarım belgesi.</summary>
    public async Task<ExportDocument?> ExportAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null)
        {
            return null;
        }

        var games = await db.LibraryGames
            .AsNoTracking()
            .Where(g => g.UserId == userId)
            .OrderBy(g => g.DateAdded)
            .Select(g => new
            {
                g.Id,
                g.Title,
                g.Description,
                g.CoverImageUrl,
                Platform = g.Platform.Name,
                Genres = g.Genres.OrderBy(x => x.Name).Select(x => x.Name).ToList(),
                Collections = g.Collections.OrderBy(x => x.Name).Select(x => x.Name).ToList(),
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
            .ToListAsync(ct);

        return new ExportDocument(
            ExportFormat,
            time.GetUtcNow().UtcDateTime,
            new ExportAccount(user.Email ?? string.Empty, user.DisplayName, user.CreatedAtUtc),
            games.Select(g => new ExportGame(
                g.Id, g.Title, g.Description, g.CoverImageUrl, g.Platform, g.Genres,
                StatusLabels.Key(g.Status), g.Rating, g.PlaytimeHours, g.IsFavorite, g.Notes,
                g.StartDate, g.CompletionDate, g.DateAdded, g.UpdatedAtUtc, g.ReleaseDate, g.RawgId, g.EstimatedHours, g.Collections)).ToList());
    }

    /// <summary>Kullanıcının tüm sunucu oturumlarını siler (isteğe bağlı olarak biri hariç).</summary>
    public Task<int> RevokeSessionsAsync(Guid userId, Guid? exceptSessionId = null, CancellationToken ct = default) =>
        db.UserSessions
            .Where(s => s.UserId == userId && (exceptSessionId == null || s.Id != exceptSessionId))
            .ExecuteDeleteAsync(ct);

    public Task<int> CountActiveSessionsAsync(Guid userId, CancellationToken ct = default)
    {
        var now = time.GetUtcNow().UtcDateTime;
        return db.UserSessions.CountAsync(s => s.UserId == userId && s.ExpiresAtUtc > now, ct);
    }

    /// <summary>
    /// Hesabı ve ilişkili tüm verileri (oyunlar, oturumlar, Identity kayıtları) tek bir
    /// transaction içinde kalıcı olarak siler. Çağıran, yeniden kimlik doğrulamayı ve
    /// açık onayı önceden yapmış olmalıdır.
    /// </summary>
    public async Task<bool> DeleteAccountAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return false;
        }

        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            // Veritabanındaki cascade kuralları da aynı sonucu verir; açık silme,
            // davranışı şemadan bağımsız ve test edilebilir kılar.
            await db.LibraryGames.Where(g => g.UserId == userId).ExecuteDeleteAsync(ct);
            // Koleksiyonlar kullanıcıya cascade ile bağlı değil (bkz. GameVaultDbContext); oyunlardan sonra açıkça silinir.
            await db.Collections.Where(c => c.UserId == userId).ExecuteDeleteAsync(ct);
            await db.UserSessions.Where(s => s.UserId == userId).ExecuteDeleteAsync(ct);
            // Kuyrukta bekleyen e-postalar (alıcı adresi ve bağlantı içerir) ve tamamlanmış işlerin takip kayıtları.
            await db.OutboxEmails.Where(e => e.UserId == userId).ExecuteDeleteAsync(ct);
            // UserOwnedLibrary migration'ının arşivi, eski kütüphane kayıtlarını (not, puan) kullanıcı kimliğiyle tutar.
            // Tablo yalnızca eski şemadan yükseltilen veritabanlarında vardır. Ortak katalog (Legacy_Games) kişisel veri değildir.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                IF OBJECT_ID(N'[Legacy_UserGames]', N'U') IS NOT NULL
                    EXEC sp_executesql N'DELETE FROM [Legacy_UserGames] WHERE [UserId] = @userId', N'@userId uniqueidentifier', @userId = {userId};
                """, ct);

            var result = await users.DeleteAsync(user);
            if (!result.Succeeded)
            {
                await transaction.RollbackAsync(ct);
                return false;
            }

            await transaction.CommitAsync(ct);
            return true;
        });
    }
}
