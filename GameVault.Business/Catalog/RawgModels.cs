namespace GameVault.Business.Catalog;

// RAWG yanıtlarının yalnızca kullanılan alanları. Bu tipler Business katmanının dışına çıkmaz;
// RawgMapping bunları doğrulayıp sağlayıcıdan bağımsız CatalogGame modeline dönüştürür.
// Alan adları snake_case olarak eşlenir (background_image → BackgroundImage).

internal sealed record RawgSearchResponse(IReadOnlyList<RawgGame>? Results);

internal sealed record RawgGame(
    int Id,
    string? Name,
    string? Released,
    string? BackgroundImage,
    IReadOnlyList<RawgPlatformEntry>? Platforms,
    IReadOnlyList<RawgNamed>? Genres);

internal sealed record RawgPlatformEntry(RawgNamed? Platform);

internal sealed record RawgNamed(string? Slug, string? Name);
