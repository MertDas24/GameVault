using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace GameVault.E2E;

public sealed class UserFlowTests(E2EFixture fixture) : IClassFixture<E2EFixture>
{
    private const string Password = "e2e-parola-12345";

    private async Task<(IBrowserContext Context, IPage Page, List<string> CspErrors)> NewPageAsync(BrowserNewContextOptions? options = null)
    {
        var context = await fixture.Browser.NewContextAsync(options ?? new BrowserNewContextOptions { ViewportSize = new() { Width = 1280, Height = 860 } });
        var page = await context.NewPageAsync();
        var errors = new List<string>();
        page.Console += (_, msg) =>
        {
            if (msg.Type == "error" && msg.Text.Contains("Content Security Policy", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add(msg.Text);
            }
        };
        return (context, page, errors);
    }

    internal async Task RegisterAsync(IPage page, string email)
    {
        await page.GotoAsync(fixture.BaseUrl + "/Account/Register");
        await page.GetByLabel("E-posta").FillAsync(email);
        await page.GetByLabel("Görünen ad").FillAsync("E2E Oyuncu");
        await page.GetByLabel("Parola", new() { Exact = true }).FillAsync(Password);
        await page.GetByLabel("Parola (tekrar)").FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Kayıt ol" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "E-postanı kontrol et" })).ToBeVisibleAsync();

        // E-postadaki doğrulama bağlantısını aç ve onayla, sonra giriş yap.
        var mail = await fixture.Emails.WaitForAsync(email, "doğrula");
        var link = System.Text.RegularExpressions.Regex.Match(mail.TextBody, @"http://localhost(/Account/ConfirmEmail\S+)").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(link), "Doğrulama e-postasında bağlantı bulunamadı.");
        await page.GotoAsync(fixture.BaseUrl + link);
        await page.GetByRole(AriaRole.Button, new() { Name = "E-postamı doğrula" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("doğrulandı");
        await page.GetByLabel("E-posta").FillAsync(email);
        await page.GetByLabel("Parola", new() { Exact = true }).FillAsync(Password);
        await page.GetByRole(AriaRole.Button, new() { Name = "Giriş yap" }).ClickAsync();
        await page.WaitForURLAsync("**/Library");
    }

    private static async Task Screenshot(IPage page, string name) =>
        await page.ScreenshotAsync(new() { Path = Path.Combine(E2EFixture.ArtifactsDir, name + ".png"), FullPage = true });

    [Fact]
    public async Task Desktop_user_can_register_add_edit_favorite_view_stats_and_logout()
    {
        var (context, page, cspErrors) = await NewPageAsync();
        await using var _ = context;

        await page.GotoAsync(fixture.BaseUrl + "/");
        await Screenshot(page, "01-landing-desktop");

        await RegisterAsync(page, $"desktop-{Guid.NewGuid():N}@example.test");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Kütüphanen boş" })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".sidebar")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".bottom-nav")).ToBeHiddenAsync();
        await Screenshot(page, "02-library-empty");

        // Oyun ekle
        await page.GetByRole(AriaRole.Link, new() { Name = "İlk oyununu ekle" }).ClickAsync();
        await page.GetByLabel("Oyun adı").FillAsync("Yıldız Tozu Kronikleri");
        await page.GetByRole(AriaRole.Combobox, new() { Name = "Platform", Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "PC" });
        await page.GetByLabel("Durum").SelectOptionAsync(new SelectOptionValue { Label = "Oynuyorum" });
        await page.GetByLabel("Rol Yapma (RPG)").CheckAsync();
        await page.GetByLabel("Oynama süresi (saat)").FillAsync("42");
        await page.GetByLabel("Kişisel puan").SelectOptionAsync(new SelectOptionValue { Label = "9 / 10" });
        await page.GetByLabel("Nebula").CheckAsync();
        await page.GetByLabel("Kişisel notlar").FillAsync("Uzay istasyonundaki bölümde kaldım.");
        await Screenshot(page, "03-add-game-form");
        await page.GetByRole(AriaRole.Button, new() { Name = "Kütüphaneye ekle" }).ClickAsync();

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Yıldız Tozu Kronikleri", Level = 1 })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("kütüphanene eklendi");
        await Assertions.Expect(page.Locator(".detail .badge").First).ToHaveTextAsync("Oynuyorum");
        await Screenshot(page, "04-game-details");

        // Doğrulama hatası formda görünür ve alan erişilebilir biçimde işaretlenir
        await page.GetByRole(AriaRole.Link, new() { Name = "Düzenle" }).ClickAsync();
        await page.GetByLabel("Oyun adı").FillAsync("");
        await page.GetByLabel("Kişisel puan").SelectOptionAsync(new SelectOptionValue { Label = "10 / 10" });
        await page.GetByRole(AriaRole.Button, new() { Name = "Değişiklikleri kaydet" }).ClickAsync();
        await Assertions.Expect(page.Locator("#Input_Title-error")).ToHaveTextAsync("Oyun adı zorunludur.");
        await Assertions.Expect(page.GetByLabel("Oyun adı")).ToHaveAttributeAsync("aria-invalid", "true");
        await Screenshot(page, "05-validation-error");

        await page.GetByLabel("Oyun adı").FillAsync("Yıldız Tozu Kronikleri: Tam Sürüm");
        await page.GetByLabel("Durum").SelectOptionAsync(new SelectOptionValue { Label = "Bitirdim" });
        await page.GetByRole(AriaRole.Button, new() { Name = "Değişiklikleri kaydet" }).ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToHaveTextAsync("Yıldız Tozu Kronikleri: Tam Sürüm");

        // Kütüphane kartından favorilere ekle
        await page.GetByRole(AriaRole.Link, new() { Name = "Kütüphane" }).First.ClickAsync();
        var fav = page.GetByRole(AriaRole.Button, new() { Name = "Favorilere ekle: Yıldız Tozu Kronikleri: Tam Sürüm" });
        await fav.ClickAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Favorilerden çıkar: Yıldız Tozu Kronikleri: Tam Sürüm" }))
            .ToHaveAttributeAsync("aria-pressed", "true");

        // Masaüstünde filtreler açık ve yan yana dizili (tek sütuna yığılmamış)
        var statusBox = await page.GetByLabel("Durum").BoundingBoxAsync();
        var platformBox = await page.GetByRole(AriaRole.Combobox, new() { Name = "Platform", Exact = true }).BoundingBoxAsync();
        Assert.NotNull(statusBox);
        Assert.NotNull(platformBox);
        Assert.Equal(statusBox.Y, platformBox.Y, 1.0);
        Assert.True(platformBox.X > statusBox.X, "Platform filtresi Durum filtresinin sağında olmalı.");

        // Arama ve filtre
        await page.GetByLabel("Ara", new() { Exact = true }).FillAsync("tam sürüm");
        await page.GetByLabel("Yalnızca favoriler").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Uygula" }).ClickAsync();
        await Assertions.Expect(page.Locator(".card")).ToHaveCountAsync(1);
        await Screenshot(page, "06-library-filtered");

        // İstatistikler gerçek veriyi gösterir
        await page.GetByRole(AriaRole.Link, new() { Name = "İstatistikler" }).First.ClickAsync();
        await Assertions.Expect(page.Locator(".stat-tile").First).ToContainTextAsync("1");
        await Assertions.Expect(page.Locator(".stat-tile").Nth(3)).ToContainTextAsync("%100");
        await Screenshot(page, "07-stats");

        // Hesap ayarları ve çıkış
        await page.GetByRole(AriaRole.Link, new() { Name = "Hesap" }).First.ClickAsync();
        await Screenshot(page, "08-settings");
        await page.Locator(".sidebar").GetByRole(AriaRole.Button, new() { Name = "Çıkış yap" }).ClickAsync();
        await page.WaitForURLAsync("**/Account/Login");
        await Assertions.Expect(page.GetByRole(AriaRole.Status)).ToContainTextAsync("Çıkış yaptın.");

        await page.GotoAsync(fixture.BaseUrl + "/Library");
        await page.WaitForURLAsync("**/Account/Login?returnUrl=%2FLibrary");

        Assert.Empty(cspErrors);
    }

    [Fact]
    public async Task Mobile_user_can_navigate_with_bottom_bar_and_add_and_delete_a_game()
    {
        var device = fixture.Browser.BrowserType.Name == "chromium"
            ? new BrowserNewContextOptions { ViewportSize = new() { Width = 390, Height = 844 }, IsMobile = true, HasTouch = true, DeviceScaleFactor = 2 }
            : new BrowserNewContextOptions { ViewportSize = new() { Width = 390, Height = 844 } };
        var (context, page, cspErrors) = await NewPageAsync(device);
        await using var _ = context;

        await RegisterAsync(page, $"mobile-{Guid.NewGuid():N}@example.test");
        await Assertions.Expect(page.Locator(".sidebar")).ToBeHiddenAsync();
        var bottomNav = page.GetByRole(AriaRole.Navigation, new() { Name = "Mobil menü" });
        await Assertions.Expect(bottomNav).ToBeVisibleAsync();
        await Screenshot(page, "10-mobile-empty");

        await bottomNav.GetByRole(AriaRole.Link, new() { Name = "Ekle" }).TapAsync();
        await page.GetByLabel("Oyun adı").FillAsync("Çok Uzun Adlı Bir Oyun: Unutulmuş Krallıkların Son Muhafızının Destansı ve Bitmek Bilmeyen Yolculuğu");
        await page.GetByRole(AriaRole.Combobox, new() { Name = "Platform", Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "Nintendo Switch" });
        await page.GetByLabel("Orman").CheckAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Kütüphaneye ekle" }).TapAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToContainTextAsync("Çok Uzun Adlı");

        await bottomNav.GetByRole(AriaRole.Link, new() { Name = "Kütüphane" }).TapAsync();
        await Assertions.Expect(page.Locator(".card")).ToHaveCountAsync(1);
        await Screenshot(page, "11-mobile-library");

        // Yatay kaydırma olmamalı (uzun başlıklar taşmamalı).
        var overflow = await page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > window.innerWidth + 1");
        Assert.False(overflow, "Sayfa yatay olarak taşıyor.");

        // Silme, açık onay sayfası üzerinden yapılır.
        await page.Locator(".card-link").First.TapAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Sil…" }).TapAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Oyunu sil" })).ToBeVisibleAsync();
        await Screenshot(page, "12-mobile-delete-confirm");
        await page.GetByRole(AriaRole.Button, new() { Name = "Evet, sil" }).TapAsync();
        await page.WaitForURLAsync("**/Library");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Kütüphanen boş" })).ToBeVisibleAsync();

        Assert.Empty(cspErrors);
    }

    [Fact]
    public async Task Keyboard_users_get_a_skip_link_and_visible_focus()
    {
        var (context, page, _) = await NewPageAsync();
        await using var __ = context;

        // Açılış sayfasında autofocus yok; ilk Tab atlama bağlantısına gitmeli.
        await page.GotoAsync(fixture.BaseUrl + "/");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        await page.Keyboard.PressAsync("Tab");
        var skip = page.GetByRole(AriaRole.Link, new() { Name = "İçeriğe geç" });
        await Assertions.Expect(skip).ToBeFocusedAsync();
        await Assertions.Expect(skip).ToBeInViewportAsync();

        var outline = await skip.EvaluateAsync<string>("el => getComputedStyle(el).outlineStyle");
        Assert.NotEqual("none", outline);

        await page.Keyboard.PressAsync("Enter");
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(page.Locator("main :focus")).ToHaveCountAsync(1);
    }

    /// <summary>
    /// Kritik sayfaları (hata durumları dahil) axe-core ile WCAG 2.0/2.1 A ve AA kurallarına göre denetler.
    /// Otomatik denetim erişilebilirlik sorunlarının yalnızca bir kısmını bulur; elle test yerine geçmez.
    /// </summary>
    [Fact]
    public async Task Critical_pages_have_no_automatically_detectable_wcag_violations()
    {
        var (context, page, _) = await NewPageAsync();
        await using var _ctx = context;
        var findings = new List<string>();

        async Task Audit(string name)
        {
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            var result = await page.RunAxe(new AxeRunOptions
            {
                RunOnly = new RunOnlyOptions { Type = "tag", Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"] }
            });
            // Sağlamlık kontrolü: axe gerçekten çalıştı mı (ör. CSP enjeksiyonu engellemedi mi)?
            if (result.Passes.Length < 10)
            {
                findings.Add($"{name}: axe yalnızca {result.Passes.Length} kuralı geçerli buldu; denetim çalışmamış olabilir.");
            }

            foreach (var v in result.Violations)
            {
                findings.Add($"{name}: [{v.Impact}] {v.Id} – {v.Help} ({string.Join(", ", v.Nodes.Take(3).Select(n => n.Target.ToString()))})");
            }
        }

        foreach (var url in new[] { "/", "/Account/Login", "/Account/Register", "/Account/ForgotPassword", "/Account/ResendConfirmation", "/Privacy", "/yok-boyle-bir-sayfa" })
        {
            await page.GotoAsync(fixture.BaseUrl + url);
            await Audit(url);
        }

        // Hata durumları
        await page.GotoAsync(fixture.BaseUrl + "/Account/Login");
        await page.GetByLabel("E-posta").FillAsync($"yok-{Guid.NewGuid():N}@example.test");
        await page.GetByLabel("Parola", new() { Exact = true }).FillAsync("yanlis-parola-1");
        await page.GetByRole(AriaRole.Button, new() { Name = "Giriş yap" }).ClickAsync();
        await Audit("login-error");

        await page.GotoAsync(fixture.BaseUrl + "/Account/Register");
        await page.GetByLabel("E-posta").FillAsync("gecersiz");
        await page.GetByRole(AriaRole.Button, new() { Name = "Kayıt ol" }).ClickAsync();
        await Audit("register-errors");

        await RegisterAsync(page, $"a11y-{Guid.NewGuid():N}@example.test");
        await Audit("/Library (boş)");

        await page.GotoAsync(fixture.BaseUrl + "/Library/New");
        await Audit("/Library/New");
        await page.GetByRole(AriaRole.Button, new() { Name = "Kütüphaneye ekle" }).ClickAsync();
        await Audit("/Library/New (hatalı)");

        await page.GetByLabel("Oyun adı").FillAsync("Erişilebilir Oyun");
        await page.GetByRole(AriaRole.Combobox, new() { Name = "Platform", Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "PC" });
        await page.GetByLabel("Nebula").CheckAsync();
        // Puan ve başlama tarihi: /Stats denetiminde grafiklerin (SVG + tablo) da çizilmesi için.
        await page.GetByLabel("Kişisel puan").SelectOptionAsync("8");
        await page.GetByLabel("Başlama tarihi").FillAsync(DateTime.UtcNow.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        await page.GetByRole(AriaRole.Button, new() { Name = "Kütüphaneye ekle" }).ClickAsync();
        await Audit("ayrıntı");
        await page.GetByRole(AriaRole.Link, new() { Name = "Sil…" }).ClickAsync();
        await Audit("silme onayı");

        foreach (var url in new[] { "/Library", "/Stats", "/Suggest", "/Suggest?maxHours=1", "/Library/Search", "/Account/Settings", "/Account/Delete", "/Account/Export", "/Account/Import" })
        {
            await page.GotoAsync(fixture.BaseUrl + url);
            await Audit(url);
        }

        // Grafiklerin tablo görünümleri açıkken de denetlenir (kapalı <details> içeriği taranmaz).
        await page.GotoAsync(fixture.BaseUrl + "/Stats");
        Assert.Equal(2, await page.Locator("svg.chart-svg").CountAsync());
        foreach (var summary in await page.Locator("details.chart-table > summary").AllAsync())
        {
            await summary.ClickAsync();
        }

        await Audit("/Stats (tablolar açık)");

        await page.SetViewportSizeAsync(390, 844);
        await page.GotoAsync(fixture.BaseUrl + "/Library");
        await Audit("/Library (mobil)");

        Assert.True(findings.Count == 0, "axe bulguları:" + Environment.NewLine + string.Join(Environment.NewLine, findings));
    }

    [Fact]
    public async Task Broken_cover_images_fall_back_to_initials()
    {
        var (context, page, _) = await NewPageAsync();
        await using var __ = context;

        await RegisterAsync(page, $"cover-{Guid.NewGuid():N}@example.test");
        await page.GotoAsync(fixture.BaseUrl + "/Library/New");
        await page.GetByLabel("Oyun adı").FillAsync("Kırık Kapak");
        await page.GetByRole(AriaRole.Combobox, new() { Name = "Platform", Exact = true }).SelectOptionAsync(new SelectOptionValue { Label = "PC" });
        await page.GetByLabel("Adres", new() { Exact = true }).CheckAsync();
        await page.GetByLabel("Görsel adresi (https)").FillAsync("https://cdn.gamevault.invalid/yok.png");
        await page.GetByRole(AriaRole.Button, new() { Name = "Kütüphaneye ekle" }).ClickAsync();

        await Assertions.Expect(page.Locator(".cover-detail")).ToHaveClassAsync(new System.Text.RegularExpressions.Regex("is-broken"));
        await Assertions.Expect(page.Locator(".cover-detail .cover-fallback")).ToContainTextAsync("KK");
        await Screenshot(page, "13-broken-cover");
    }

    /// <summary>
    /// Gerçek Kestrel'de: uygulamanın genel 128 KB gövde sınırından büyük bir dosya, yalnızca içe aktarma sayfasında
    /// yükseltilen sınır sayesinde kabul edilir. (TestServer bu sınırı uygulamadığı için bu kontrol burada yapılır.)
    /// </summary>
    [Fact]
    public async Task User_can_check_and_import_a_file_larger_than_the_default_body_limit()
    {
        var (context, page, cspErrors) = await NewPageAsync();
        await using var __ = context;

        await RegisterAsync(page, $"import-{Guid.NewGuid():N}@example.test");

        var games = new System.Text.Json.Nodes.JsonArray();
        for (var i = 1; i <= 80; i++)
        {
            games.Add(new System.Text.Json.Nodes.JsonObject
            {
                ["title"] = $"Büyük Dosya Oyunu {i}",
                ["platform"] = "PC",
                ["status"] = "not-started",
                ["description"] = new string('a', 3900),
                ["notes"] = new string('n', 3900)
            });
        }

        var json = new System.Text.Json.Nodes.JsonObject { ["format"] = "gamevault-export/v1", ["games"] = games }.ToJsonString();
        var bytes = System.Text.Encoding.UTF8.GetBytes(json);
        Assert.True(bytes.Length > 512 * 1024, "Dosya genel sınırdan belirgin biçimde büyük olmalı.");
        var file = new FilePayload { Name = "gamevault-export.json", MimeType = "application/json", Buffer = bytes };

        await page.GotoAsync(fixture.BaseUrl + "/Account/Import");
        await page.GetByLabel("JSON dosyası").SetInputFilesAsync(file);
        await page.GetByRole(AriaRole.Button, new() { Name = "Kontrol et" }).ClickAsync();
        await Assertions.Expect(page.Locator("#import-result")).ToContainTextAsync("80 tanesi eklenebilir");

        await page.GetByLabel("JSON dosyası").SetInputFilesAsync(file);
        await page.GetByRole(AriaRole.Button, new() { Name = "İçe aktar" }).ClickAsync();
        await Assertions.Expect(page.Locator("#import-result")).ToContainTextAsync("80 oyun kütüphanene eklendi");
        await Screenshot(page, "14-import");

        await page.GotoAsync(fixture.BaseUrl + "/Library?q=B%C3%BCy%C3%BCk%20Dosya");
        await Assertions.Expect(page.GetByText("80 oyun")).ToBeVisibleAsync();
        Assert.Empty(cspErrors);
    }
}
