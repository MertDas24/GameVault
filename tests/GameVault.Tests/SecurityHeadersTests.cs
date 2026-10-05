using System.Net;
using GameVault.Tests.Infrastructure;

namespace GameVault.Tests;

public sealed class SecurityHeadersTests(GameVaultFactory factory) : IClassFixture<GameVaultFactory>
{
    [Fact]
    public async Task Pages_send_security_headers_and_strict_csp()
    {
        using var client = factory.CreateAppClient();
        var response = await client.Http.GetAsync("/Account/Login");

        var csp = string.Join(' ', response.Headers.GetValues("Content-Security-Policy"));
        Assert.Contains("default-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("script-src 'self'", csp, StringComparison.Ordinal);
        Assert.Contains("frame-ancestors 'none'", csp, StringComparison.Ordinal);
        Assert.Contains("img-src 'self' https://images.example.com", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-inline", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", csp, StringComparison.Ordinal);

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("same-origin", response.Headers.GetValues("Referrer-Policy").Single());
        Assert.False(response.Headers.Contains("Server"));

        // HSTS yalnızca Production ortamında ve HTTPS'te gönderilir.
        Assert.False(response.Headers.Contains("Strict-Transport-Security"));
    }

    [Fact]
    public async Task Html_pages_have_no_inline_scripts_or_styles()
    {
        using var client = factory.CreateAppClient();
        await client.RegisterAsync($"csp-{Guid.NewGuid():N}@example.test");
        foreach (var url in new[] { "/Library", "/Library/New", "/Stats", "/Account/Settings" })
        {
            var (_, doc) = await client.GetPageAsync(url);
            Assert.All(doc.QuerySelectorAll("script"), s => Assert.True(s.HasAttribute("src"), $"{url}: satır içi script"));
            Assert.Empty(doc.QuerySelectorAll("style"));
            Assert.Empty(doc.QuerySelectorAll("[style]"));
            Assert.Empty(doc.QuerySelectorAll("[onclick],[onerror],[onload],[onsubmit]"));
        }
    }

    [Fact]
    public async Task Authenticated_pages_are_not_cacheable()
    {
        using var client = factory.CreateAppClient();
        await client.RegisterAsync($"cache-{Guid.NewGuid():N}@example.test");

        var response = await client.Http.GetAsync("/Library");
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Versioned_static_assets_are_cacheable()
    {
        using var client = factory.CreateAppClient();
        var (_, doc) = await client.GetPageAsync("/");
        var css = doc.QuerySelector("link[rel=stylesheet]")!.GetAttribute("href")!;
        Assert.Contains("?v=", css, StringComparison.Ordinal);

        var response = await client.Http.GetAsync(css);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("immutable", response.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_endpoint_reveals_no_details()
    {
        using var client = factory.CreateAppClient();
        var response = await client.Http.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        Assert.Equal("Healthy", await client.Http.GetStringAsync("/health/live"));
    }

    [Fact]
    public async Task Not_found_page_is_friendly_and_generic()
    {
        using var client = factory.CreateAppClient();
        await client.RegisterAsync($"nf-{Guid.NewGuid():N}@example.test");

        var response = await client.Http.GetAsync($"/Library/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Sayfa bulunamadı", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Oversized_request_body_is_rejected()
    {
        using var client = factory.CreateAppClient();
        await client.RegisterAsync($"big-{Guid.NewGuid():N}@example.test");
        var token = await client.GetTokenAsync("/Library/New");

        var response = await client.Http.PostAsync("/Library/New", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token,
            ["Input.Title"] = "Büyük",
            ["Input.Notes"] = new string('a', 400_000)
        }));

        Assert.True(response.StatusCode is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadRequest,
            $"Beklenmeyen durum: {response.StatusCode}");
    }
}
