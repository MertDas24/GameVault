using System.Globalization;
using System.Net;
using GameVault.Business.Stats;
using GameVault.Models.Stats;
using GameVault.Tests.Infrastructure;
using GameVault.Web.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Tests;

/// <summary>İstatistik grafikleri: puan dağılımı, aylık etkinlik penceresi ve sunucuda çizilen SVG.</summary>
public sealed class StatsTests(GameVaultFactory factory) : IClassFixture<GameVaultFactory>
{
    private async Task<(AppClient Client, Guid Id, int Pc)> NewUserAsync()
    {
        var email = $"stats-{Guid.NewGuid():N}@example.test";
        var client = factory.CreateAppClient();
        await client.RegisterAsync(email);
        var id = await factory.WithDbAsync(db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());
        return (client, id, await client.GetPlatformIdAsync());
    }

    /// <summary>Tarihleri doğrudan veritabanına yazar; böylece test, form doğrulamasının "bugün" sınırına bağlı kalmaz.</summary>
    private Task SetDatesAsync(Guid gameId, DateOnly? start, DateOnly? completion) =>
        factory.WithDbAsync(async db =>
        {
            var game = await db.LibraryGames.SingleAsync(g => g.Id == gameId);
            game.StartDate = start;
            game.CompletionDate = completion;
            await db.SaveChangesAsync();
        });

    private async Task<LibraryStats> GetStatsAsync(Guid userId, DateOnly today)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<StatsService>().GetAsync(userId, today);
    }

    [Fact]
    public async Task Rating_distribution_counts_each_score_and_ignores_unrated_games()
    {
        var (client, userId, pc) = await NewUserAsync();
        using (client)
        {
            await client.CreateGameAsync("Sekiz A", pc, "Completed", ("Input.Rating", "8"));
            await client.CreateGameAsync("Sekiz B", pc, "Playing", ("Input.Rating", "8"));
            await client.CreateGameAsync("On", pc, "Completed", ("Input.Rating", "10"));
            await client.CreateGameAsync("Puansız", pc);

            var stats = await GetStatsAsync(userId, new DateOnly(2026, 10, 6));

            Assert.Equal(LibraryStats.RatingScale, stats.RatingDistribution.Count);
            Assert.Equal(2, stats.RatingDistribution[7]);
            Assert.Equal(1, stats.RatingDistribution[9]);
            Assert.Equal(3, stats.RatingDistribution.Sum());
            Assert.Equal(stats.RatedGames, stats.RatingDistribution.Sum());
        }
    }

    [Fact]
    public async Task Monthly_activity_covers_the_last_twelve_months_including_the_current_one()
    {
        var (client, userId, pc) = await NewUserAsync();
        using (client)
        {
            var today = new DateOnly(2026, 10, 6);
            var inWindow = await client.CreateGameAsync("Pencerede", pc, "Completed");
            var firstMonth = await client.CreateGameAsync("İlk ay", pc, "Playing");
            var tooOld = await client.CreateGameAsync("Çok eski", pc, "Completed");
            var future = await client.CreateGameAsync("Gelecek ay", pc, "Playing");
            await SetDatesAsync(inWindow, new DateOnly(2026, 8, 31), new DateOnly(2026, 10, 1));
            await SetDatesAsync(firstMonth, new DateOnly(2025, 11, 1), null);
            await SetDatesAsync(tooOld, new DateOnly(2025, 10, 31), new DateOnly(2025, 10, 31));
            await SetDatesAsync(future, new DateOnly(2026, 11, 1), null);

            var activity = (await GetStatsAsync(userId, today)).MonthlyActivity;

            Assert.Equal(LibraryStats.ActivityMonths, activity.Count);
            Assert.Equal(new DateOnly(2025, 11, 1), activity[0].Month);
            Assert.Equal(new DateOnly(2026, 10, 1), activity[^1].Month);
            Assert.Equal(1, activity[0].Started);
            Assert.Equal(1, activity.Single(m => m.Month == new DateOnly(2026, 8, 1)).Started);
            Assert.Equal(1, activity[^1].Completed);
            Assert.Equal(2, activity.Sum(m => m.Started));
            Assert.Equal(1, activity.Sum(m => m.Completed));
        }
    }

    [Fact]
    public async Task Stats_page_renders_charts_with_table_views_and_no_inline_styles()
    {
        var (client, _, pc) = await NewUserAsync();
        using (client)
        {
            await client.CreateGameAsync("Grafik Oyunu", pc, "Playing", ("Input.Rating", "7"),
                ("Input.StartDate", DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

            var (response, doc) = await client.GetPageAsync("/Stats");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.NotNull(doc.QuerySelector("svg[role=img][aria-labelledby=rating-chart-title]"));
            Assert.NotNull(doc.QuerySelector("svg[role=img][aria-labelledby=activity-chart-title]"));

            var tables = doc.QuerySelectorAll("details.chart-table table");
            Assert.Equal(2, tables.Length);
            var activityRows = tables[0].QuerySelectorAll("tbody tr");
            Assert.Equal(LibraryStats.ActivityMonths, activityRows.Length);
            var ratingRow = tables[1].QuerySelectorAll("tbody tr").Single(r => r.QuerySelector("th")!.TextContent == "7 puan");
            Assert.Equal("1", ratingRow.QuerySelector("td")!.TextContent);

            // CSP satır içi stile izin vermez; grafikler yalnızca SVG öznitelikleri ve CSS sınıflarıyla çizilir.
            Assert.Empty(doc.QuerySelectorAll("[style]"));
        }
    }

    [Fact]
    public async Task Charts_show_an_explanation_instead_of_an_empty_plot()
    {
        var (client, _, pc) = await NewUserAsync();
        using (client)
        {
            await client.CreateGameAsync("Tarihsiz ve puansız", pc);

            var (_, doc) = await client.GetPageAsync("/Stats");

            Assert.Empty(doc.QuerySelectorAll("svg.chart-svg"));
            Assert.Contains("Henüz hiçbir oyuna puan vermedin.", doc.QuerySelector("#ratings")!.TextContent);
            Assert.Contains("başlama ya da bitirme tarihi girilmiş bir oyun yok", doc.QuerySelector("#activity")!.TextContent);
        }
    }
}

public sealed class ChartScaleTests
{
    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(1, 1, 1)]
    [InlineData(4, 4, 1)]
    [InlineData(5, 6, 2)]
    [InlineData(7, 8, 2)]
    [InlineData(9, 10, 5)]
    [InlineData(23, 30, 10)]
    [InlineData(41, 60, 20)]
    [InlineData(150, 150, 50)]
    [InlineData(151, 200, 50)]
    public void Axis_uses_at_most_four_round_intervals(int max, int expectedTop, int expectedStep)
    {
        var (top, step) = ChartScale.Axis(max);
        Assert.Equal(expectedTop, top);
        Assert.Equal(expectedStep, step);
        Assert.True(top >= max);
        Assert.True(top / step <= 4);
    }

    [Fact]
    public void Lengths_scale_to_the_axis_and_never_overflow_it()
    {
        Assert.Equal(0, ChartScale.Length(0, 10, 160));
        Assert.Equal(80, ChartScale.Length(5, 10, 160));
        Assert.Equal(160, ChartScale.Length(10, 10, 160));
        Assert.Equal(160, ChartScale.Length(12, 10, 160));
    }
}
