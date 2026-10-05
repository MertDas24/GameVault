using GameVault.Business.Library;
using Microsoft.Playwright;

namespace GameVault.E2E;

/// <summary>
/// README ekran görüntülerini üretir. Normal test çalıştırmasında atlanır; yalnızca
/// GAMEVAULT_SCREENSHOTS_DIR ortam değişkeni verildiğinde çalışır. Veri tamamen kurgusaldır
/// (DemoDataSeeder) ve geçici test veritabanında oluşturulur.
/// </summary>
public sealed class ReadmeScreenshots(E2EFixture fixture) : IClassFixture<E2EFixture>
{
    [Fact]
    public async Task Capture_readme_screenshots()
    {
        var outDir = Environment.GetEnvironmentVariable("GAMEVAULT_SCREENSHOTS_DIR");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(outDir), "GAMEVAULT_SCREENSHOTS_DIR tanımlı değil.");
        Directory.CreateDirectory(outDir!);

        var password = "demo-" + Guid.NewGuid().ToString("N");
        await fixture.SeedDemoAsync(password);

        var desktop = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new() { Width = 1440, Height = 900 },
            ReducedMotion = ReducedMotion.Reduce
        });
        await using (desktop)
        {
            var page = await desktop.NewPageAsync();
            await page.GotoAsync(fixture.BaseUrl + "/");
            await Shot(page, outDir!, "landing");

            await LoginAsync(page, password);
            await Shot(page, outDir!, "library");

            await page.GetByRole(AriaRole.Link, new() { Name = "Zindan Ustası: Sonsuz Katlar" }).First.ClickAsync();
            await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Zindan Ustası: Sonsuz Katlar", Level = 1 })).ToBeVisibleAsync();
            await Shot(page, outDir!, "details");

            await page.GotoAsync(fixture.BaseUrl + "/Library/New");
            await Shot(page, outDir!, "add-game");

            await page.GotoAsync(fixture.BaseUrl + "/Stats");
            await Shot(page, outDir!, "stats");
        }

        var mobile = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new() { Width = 390, Height = 844 },
            DeviceScaleFactor = 2,
            IsMobile = true,
            HasTouch = true,
            ReducedMotion = ReducedMotion.Reduce
        });
        await using (mobile)
        {
            var page = await mobile.NewPageAsync();
            await LoginAsync(page, password);
            await Shot(page, outDir!, "mobile-library");
        }
    }

    private async Task LoginAsync(IPage page, string password)
    {
        await page.GotoAsync(fixture.BaseUrl + "/Account/Login");
        await page.GetByLabel("E-posta").FillAsync(DemoDataSeeder.DemoEmail);
        await page.GetByLabel("Parola", new() { Exact = true }).FillAsync(password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Giriş yap" }).ClickAsync();
        await page.WaitForURLAsync("**/Library");
    }

    private static async Task Shot(IPage page, string dir, string name)
    {
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.ScreenshotAsync(new() { Path = Path.Combine(dir, name + ".png") });
    }
}
