using System.Globalization;
using GameVault.Business.Stats;
using GameVault.Models.Stats;
using GameVault.Web.Infrastructure;

namespace GameVault.Web.Pages.Stats;

public sealed class IndexModel(StatsService stats, Display display, TimeProvider time) : AppPageModel
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public LibraryStats Stats { get; private set; } = null!;

    public ColumnChart RatingChart { get; private set; } = null!;

    public ColumnChart ActivityChart { get; private set; } = null!;

    /// <summary>Etkinlik grafiğinin kapsadığı dönem, ör. "Kasım 2025 – Ekim 2026".</summary>
    public string ActivityRange { get; private set; } = string.Empty;

    public bool HasActivity => Stats.MonthlyActivity.Any(m => m.Started > 0 || m.Completed > 0);

    public async Task OnGetAsync(CancellationToken ct)
    {
        Stats = await stats.GetAsync(CurrentUserId, display.Today(time.GetUtcNow()), ct);

        RatingChart = new ColumnChart(
            "rating-chart",
            "Puan dağılımı",
            "Puan",
            [new ChartSeries("Oyun", "series-1")],
            Stats.RatingDistribution
                .Select((count, i) => new ColumnGroup((i + 1).ToString(Turkish), $"{i + 1} puan", [count]))
                .ToList());

        ActivityChart = new ColumnChart(
            "activity-chart",
            "Son 12 ayda başlanan ve bitirilen oyunlar",
            "Ay",
            [new ChartSeries("Başlanan", "series-1"), new ChartSeries("Bitirilen", "series-2")],
            Stats.MonthlyActivity
                .Select(m => new ColumnGroup(m.Month.ToString("MMM", Turkish), m.Month.ToString("MMMM yyyy", Turkish), [m.Started, m.Completed]))
                .ToList(),
            ThinLabels: true);

        ActivityRange = $"{Stats.MonthlyActivity[0].Month.ToString("MMMM yyyy", Turkish)} – {Stats.MonthlyActivity[^1].Month.ToString("MMMM yyyy", Turkish)}";
    }
}
