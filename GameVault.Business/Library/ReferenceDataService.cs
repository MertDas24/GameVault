using GameVault.DataAccess;
using GameVault.Models.Library;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace GameVault.Business.Library;

/// <summary>
/// Platform ve tür listeleri. Bunlar kullanıcıya özel olmayan, herkes için aynı başvuru
/// verileri olduğundan süreç içi önbellekte tutulabilir.
/// </summary>
public sealed class ReferenceDataService(GameVaultDbContext db, IMemoryCache cache)
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);

    public Task<IReadOnlyList<ReferenceItem>> GetPlatformsAsync(CancellationToken ct = default) =>
        GetOrLoadAsync("ref:platforms", () =>
            db.Platforms.AsNoTracking().OrderBy(p => p.Name).Select(p => new ReferenceItem(p.Id, p.Name)).ToListAsync(ct));

    public Task<IReadOnlyList<ReferenceItem>> GetGenresAsync(CancellationToken ct = default) =>
        GetOrLoadAsync("ref:genres", () =>
            db.Genres.AsNoTracking().OrderBy(g => g.Name).Select(g => new ReferenceItem(g.Id, g.Name)).ToListAsync(ct));

    private async Task<IReadOnlyList<ReferenceItem>> GetOrLoadAsync(string key, Func<Task<List<ReferenceItem>>> load)
    {
        if (cache.TryGetValue(key, out IReadOnlyList<ReferenceItem>? cached) && cached is not null)
        {
            return cached;
        }

        var items = await load();
        cache.Set(key, (IReadOnlyList<ReferenceItem>)items, CacheDuration);
        return items;
    }
}
