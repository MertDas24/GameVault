using Microsoft.Playwright;

namespace GameVault.E2E;

/// <summary>
/// README ve LinkedIn için kısa tanıtım videosu (WebM) kaydeder. Normal test çalıştırmasında atlanır;
/// yalnızca GAMEVAULT_DEMO_VIDEO_DIR ortam değişkeni verildiğinde çalışır. Veri tamamen kurgusaldır
/// (DemoDataSeeder) ve geçici test veritabanında oluşturulur. GIF/MP4'e dönüştürme: tools/make-demo.ps1.
/// </summary>
public sealed class DemoVideo(E2EFixture fixture) : IClassFixture<E2EFixture>
{
    // Headless kayıtta fare imleci görünmez; izleyicinin tıklamaları takip edebilmesi için yalnızca bu kayıtta
    // sayfaya bir imleç çizilir. Uygulamanın kendisine hiçbir şey eklenmez.
    private const string CursorScript = """
        window.addEventListener('DOMContentLoaded', () => {
          const c = document.createElement('div');
          Object.assign(c.style, {
            position: 'fixed', left: '0', top: '0', width: '18px', height: '18px', margin: '-9px 0 0 -9px',
            borderRadius: '50%', background: 'rgba(214, 51, 108, .55)', border: '2px solid #fff',
            boxShadow: '0 0 0 1px rgba(214, 51, 108, .9)', zIndex: '2147483647', pointerEvents: 'none',
            transition: 'transform .12s ease'
          });
          document.body.appendChild(c);
          const pos = sessionStorage.getItem('demo-cursor');
          if (pos) { const [x, y] = pos.split(','); c.style.left = x + 'px'; c.style.top = y + 'px'; }
          document.addEventListener('mousemove', e => {
            c.style.left = e.clientX + 'px'; c.style.top = e.clientY + 'px';
            sessionStorage.setItem('demo-cursor', e.clientX + ',' + e.clientY);
          });
          document.addEventListener('mousedown', () => c.style.transform = 'scale(.7)');
          document.addEventListener('mouseup', () => c.style.transform = '');
        });
        """;

    [Fact]
    public async Task Record_demo_video()
    {
        var outDir = Environment.GetEnvironmentVariable("GAMEVAULT_DEMO_VIDEO_DIR");
        Assert.SkipWhen(string.IsNullOrWhiteSpace(outDir), "GAMEVAULT_DEMO_VIDEO_DIR tanımlı değil.");
        Directory.CreateDirectory(outDir!);

        var password = "demo-" + Guid.NewGuid().ToString("N");
        await fixture.SeedDemoAsync(password);

        var size = new RecordVideoSize { Width = 1280, Height = 720 };
        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new() { Width = size.Width, Height = size.Height },
            RecordVideoDir = outDir,
            RecordVideoSize = size,
            // Yalnızca imleç betiği için; uygulamanın CSP'si testlerde ayrıca doğrulanır.
            BypassCSP = true
        });
        await context.AddInitScriptAsync(CursorScript);
        var page = await context.NewPageAsync();

        // 1. Giriş
        await page.GotoAsync(fixture.BaseUrl + "/");
        await Pause(page, 1500);
        await Click(page, page.GetByRole(AriaRole.Link, new() { Name = "Giriş yap" }).First);
        await Type(page, page.GetByLabel("E-posta"), Business.Library.DemoDataSeeder.DemoEmail);
        await Type(page, page.GetByLabel("Parola", new() { Exact = true }), password, delay: 15);
        await Click(page, page.GetByRole(AriaRole.Button, new() { Name = "Giriş yap" }));
        await page.WaitForURLAsync("**/Library");
        await Pause(page, 1500);

        // 2. Kütüphane: filtre, ardından karttan koleksiyona ekleme
        await Click(page, page.GetByLabel("Durum"));
        await page.GetByLabel("Durum").SelectOptionAsync(new SelectOptionValue { Label = "Başlamadım" });
        await Click(page, page.GetByRole(AriaRole.Button, new() { Name = "Uygula" }));
        await Pause(page, 1500);
        await Click(page, page.GetByRole(AriaRole.Link, new() { Name = "Temizle" }));
        await Pause(page, 800);
        var card = page.Locator(".card").Nth(1);
        await card.ScrollIntoViewIfNeededAsync();
        await Click(page, card.Locator(".card-collections > summary"));
        await Pause(page, 900);
        await Click(page, card.Locator("button.collection-toggle").First);
        await Pause(page, 1500);

        // 3. Oyun ekleme
        await Click(page, page.Locator(".sidebar").GetByRole(AriaRole.Link, new() { Name = "Oyun ekle" }));
        await Type(page, page.GetByLabel("Oyun adı"), "Gece Yarısı Dedektifi");
        await page.GetByRole(AriaRole.Combobox, new() { Name = "Platform", Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "PC" });
        await Click(page, page.GetByLabel("Durum"));
        await page.GetByLabel("Durum").SelectOptionAsync(new SelectOptionValue { Label = "Oynuyorum" });
        await Click(page, page.GetByLabel("Macera"));
        await page.GetByLabel("Kişisel puan").SelectOptionAsync(new SelectOptionValue { Label = "8 / 10" });
        await Click(page, page.GetByLabel("Nebula"));
        await Pause(page, 600);
        await Click(page, page.GetByRole(AriaRole.Button, new() { Name = "Kütüphaneye ekle" }));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Gece Yarısı Dedektifi", Level = 1 })).ToBeVisibleAsync();
        await Pause(page, 1800);

        // 4. Ne oynasam?
        await Click(page, page.Locator(".sidebar").GetByRole(AriaRole.Link, new() { Name = "Ne oynasam?" }));
        await Pause(page, 1600);
        var another = page.GetByRole(AriaRole.Link, new() { Name = "Başka öner" });
        if (await another.CountAsync() > 0)
        {
            await Click(page, another);
            await Pause(page, 1600);
        }

        // 5. İstatistikler: yavaşça aşağı kaydır
        await Click(page, page.Locator(".sidebar").GetByRole(AriaRole.Link, new() { Name = "İstatistikler" }));
        await Pause(page, 1500);
        for (var i = 0; i < 14; i++)
        {
            await page.Mouse.WheelAsync(0, 120);
            await Pause(page, 180);
        }

        await Pause(page, 1800);

        var video = page.Video!;
        await context.CloseAsync();
        await video.SaveAsAsync(Path.Combine(outDir!, "demo.webm"));
        await video.DeleteAsync();
    }

    private static Task Pause(IPage page, int ms) => page.WaitForTimeoutAsync(ms);

    /// <summary>İmleci öğeye yavaşça götürüp tıklar; izleyici nereye tıklandığını görebilsin.</summary>
    private static async Task Click(IPage page, ILocator target)
    {
        await target.ScrollIntoViewIfNeededAsync();
        var box = await target.BoundingBoxAsync() ?? throw new InvalidOperationException("Öğe görünür değil.");
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2, new() { Steps = 18 });
        await Pause(page, 250);
        await target.ClickAsync();
        await Pause(page, 350);
    }

    private static async Task Type(IPage page, ILocator target, string text, int delay = 55)
    {
        await Click(page, target);
        await target.PressSequentiallyAsync(text, new() { Delay = delay });
        await Pause(page, 300);
    }
}
