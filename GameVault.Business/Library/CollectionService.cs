using GameVault.DataAccess;
using GameVault.Entities;
using GameVault.Models.Library;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Business.Library;

/// <summary>
/// Koleksiyonların listelenmesi, oluşturulması, yeniden adlandırılması ve silinmesi. Her sorgu çağıranın
/// kimliğiyle süzülür; başka kullanıcıya ait bir koleksiyon "bulunamadı" olarak döner.
/// Oyunlarla ilişkilendirme <see cref="LibraryService"/> üzerinden (oyun formu) yapılır.
/// </summary>
public sealed class CollectionService(GameVaultDbContext db, TimeProvider time)
{
    public const string DuplicateNameError = "Bu adla bir koleksiyonun zaten var.";

    private IQueryable<GameCollection> OwnedBy(Guid userId) => db.Collections.Where(c => c.UserId == userId);

    public async Task<IReadOnlyList<CollectionSummary>> ListAsync(Guid userId, CancellationToken ct = default) =>
        await OwnedBy(userId)
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CollectionSummary(c.Id, c.Name, c.Games.Count))
            .ToListAsync(ct);

    public async Task<CollectionSummary?> GetAsync(Guid userId, int id, CancellationToken ct = default) =>
        await OwnedBy(userId)
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CollectionSummary(c.Id, c.Name, c.Games.Count))
            .FirstOrDefaultAsync(ct);

    public async Task<OperationResult<int>> CreateAsync(Guid userId, string? name, CancellationToken ct = default)
    {
        var normalized = CollectionRules.Normalize(name ?? string.Empty);
        if (CollectionRules.NameError(normalized) is { } error)
        {
            return OperationResult<int>.Invalid("Name", error);
        }

        if (await OwnedBy(userId).CountAsync(ct) >= CollectionRules.MaxPerUser)
        {
            return OperationResult<int>.Invalid("Name", $"En fazla {CollectionRules.MaxPerUser} koleksiyon oluşturabilirsin.");
        }

        // Karşılaştırma veritabanı harmanlamasıyla (büyük/küçük harf duyarsız) yapılır; benzersiz indeks de aynı kuralı uygular.
        if (await OwnedBy(userId).AnyAsync(c => c.Name == normalized, ct))
        {
            return OperationResult<int>.Invalid("Name", DuplicateNameError);
        }

        var collection = new GameCollection { UserId = userId, Name = normalized, CreatedAtUtc = time.GetUtcNow().UtcDateTime };
        db.Collections.Add(collection);
        if (!await TrySaveAsync(ct))
        {
            return OperationResult<int>.Invalid("Name", DuplicateNameError);
        }

        return OperationResult<int>.Success(collection.Id);
    }

    public async Task<OperationResult<int>> RenameAsync(Guid userId, int id, string? name, CancellationToken ct = default)
    {
        var collection = await OwnedBy(userId).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (collection is null)
        {
            return OperationResult<int>.Missing();
        }

        var normalized = CollectionRules.Normalize(name ?? string.Empty);
        if (CollectionRules.NameError(normalized) is { } error)
        {
            return OperationResult<int>.Invalid("Name", error);
        }

        if (await OwnedBy(userId).AnyAsync(c => c.Id != id && c.Name == normalized, ct))
        {
            return OperationResult<int>.Invalid("Name", DuplicateNameError);
        }

        collection.Name = normalized;
        if (!await TrySaveAsync(ct))
        {
            return OperationResult<int>.Invalid("Name", DuplicateNameError);
        }

        return OperationResult<int>.Success(id);
    }

    /// <summary>Koleksiyonu siler; içindeki oyunlar kütüphanede kalır (yalnızca bağlantılar silinir).</summary>
    public async Task<bool> DeleteAsync(Guid userId, int id, CancellationToken ct = default) =>
        await OwnedBy(userId).Where(c => c.Id == id).ExecuteDeleteAsync(ct) > 0;

    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException { Number: 2601 or 2627 })
        {
            // Aynı anda gelen iki istek: ön kontrolü ikisi de geçti, benzersiz indeks ikincisini durdurdu.
            return false;
        }
    }
}
