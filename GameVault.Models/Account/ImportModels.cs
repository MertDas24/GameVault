using GameVault.Models.Library;

namespace GameVault.Models.Account;

/// <summary>Dosyadaki bir oyunun, platform/tür adları kimliklere çevrildikten sonraki hâli.</summary>
/// <param name="Position">Dosyadaki sırası (1'den başlar); kullanıcıya gösterilen raporda kullanılır.</param>
/// <param name="DateAddedUtc">Dosyadaki eklenme zamanı; makul değilse yok sayılır ve şimdiki zaman kullanılır.</param>
public sealed record ImportCandidate(int Position, GameInput Input, DateTime? DateAddedUtc);

/// <summary>Raporlanan tek bir satır: atlanan (zaten var) ya da hatalı oyun.</summary>
public sealed record ImportIssue(int Position, string Title, string Message);

/// <summary>İçe aktarma (ya da yalnızca kontrol) sonucu.</summary>
public sealed record ImportResult(
    bool DryRun,
    int Total,
    int Imported,
    IReadOnlyList<ImportIssue> Skipped,
    IReadOnlyList<ImportIssue> Errors)
{
    /// <summary>Kontrol modunda eklenebilecek, gerçek modda eklenmiş oyun sayısı.</summary>
    public int Importable => Total - Skipped.Count - Errors.Count;
}
