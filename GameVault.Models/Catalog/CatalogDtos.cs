namespace GameVault.Models.Catalog;

/// <summary>
/// Harici oyun kataloğundan (şu an RAWG) gelen, doğrulanmış ve sağlayıcıdan bağımsız oyun bilgisi.
/// Sayfalar yalnızca bu modeli görür; RAWG'ın yanıt biçimi Business katmanının dışına çıkmaz.
/// </summary>
public sealed record CatalogGame(
    int RawgId,
    string Name,
    string? CoverUrl,
    DateOnly? Released,
    IReadOnlyList<CatalogTag> Platforms,
    IReadOnlyList<CatalogTag> Genres,
    int? EstimatedHours = null);

/// <summary>Katalogdaki bir platform ya da tür: kararlı kısa ad (slug) ve görünen ad.</summary>
public sealed record CatalogTag(string Slug, string Name);

public enum CatalogStatus
{
    /// <summary>İstek başarılı.</summary>
    Ok,

    /// <summary>Bu kurulumda entegrasyon kapalı (API anahtarı tanımlı değil).</summary>
    Disabled,

    /// <summary>Katalog şu an kullanılamıyor (zaman aşımı, ağ hatası, hatalı yanıt).</summary>
    Unavailable,

    /// <summary>Aranan oyun bulunamadı.</summary>
    NotFound
}

public sealed record CatalogSearchResult(CatalogStatus Status, IReadOnlyList<CatalogGame> Games)
{
    public static CatalogSearchResult Of(CatalogStatus status) => new(status, []);
}

public sealed record CatalogGameResult(CatalogStatus Status, CatalogGame? Game);
