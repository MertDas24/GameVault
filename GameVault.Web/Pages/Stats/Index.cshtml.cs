using GameVault.Business.Stats;
using GameVault.Models.Stats;

namespace GameVault.Web.Pages.Stats;

public sealed class IndexModel(StatsService stats) : AppPageModel
{
    public LibraryStats Stats { get; private set; } = null!;

    public async Task OnGetAsync(CancellationToken ct) => Stats = await stats.GetAsync(CurrentUserId, ct);
}
