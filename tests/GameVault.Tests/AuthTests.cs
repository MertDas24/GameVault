using System.Net;
using GameVault.Tests.Infrastructure;
using GameVault.Web.Pages.Account;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Tests;

public sealed class AuthTests(GameVaultFactory factory) : IClassFixture<GameVaultFactory>
{
    private static string NewEmail() => $"user-{Guid.NewGuid():N}@example.test";

    [Fact]
    public async Task Register_creates_account_signs_in_and_redirects_to_library()
    {
        using var client = factory.CreateAppClient();
        var email = NewEmail();

        var response = await client.RegisterAsync(email, displayName: "Oyuncu");

        response.AssertRedirectTo("/Library");
        var (library, doc) = await client.GetPageAsync("/Library");
        Assert.Equal(HttpStatusCode.OK, library.StatusCode);
        Assert.Contains("Oyuncu", doc.QuerySelector(".sidebar-user")!.TextContent, StringComparison.Ordinal);
        Assert.Equal(1, await factory.WithDbAsync(db => db.UserSessions.CountAsync(s => db.Users.Any(u => u.Id == s.UserId && u.Email == email))));
    }

    [Fact]
    public async Task Register_rejects_short_password_and_mismatched_confirmation()
    {
        using var client = factory.CreateAppClient();

        var shortPassword = await client.SubmitRegistrationAsync(NewEmail(), password: "kisa");
        Assert.Equal(HttpStatusCode.OK, shortPassword.StatusCode);
        var doc = await client.ParseAsync(shortPassword);
        Assert.NotNull(doc.QuerySelector("#Input_Password[aria-invalid=true]"));

        var mismatch = await client.PostFormAsync("/Account/Register", new Dictionary<string, string>
        {
            ["Input.Email"] = NewEmail(),
            ["Input.Password"] = "uzun-bir-parola-1",
            ["Input.ConfirmPassword"] = "farkli-bir-parola-2"
        });
        Assert.Contains("Parolalar eşleşmiyor.", await mismatch.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Login_succeeds_with_correct_password_and_honours_local_return_url()
    {
        var email = NewEmail();
        using (var setup = factory.CreateAppClient())
        {
            await setup.RegisterAsync(email);
        }

        using var client = factory.CreateAppClient();
        var response = await client.LoginAsync(email, returnUrl: "/Stats");

        response.AssertRedirectTo("/Stats");
    }

    [Theory]
    [InlineData("https://evil.example/phish")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    public async Task Login_ignores_external_return_urls(string returnUrl)
    {
        var email = NewEmail();
        using (var setup = factory.CreateAppClient())
        {
            await setup.RegisterAsync(email);
        }

        using var client = factory.CreateAppClient();
        var response = await client.LoginAsync(email, returnUrl: returnUrl);

        response.AssertRedirectTo("/Library");
    }

    [Fact]
    public async Task Login_failure_message_is_identical_for_unknown_email_and_wrong_password()
    {
        var email = NewEmail();
        using (var setup = factory.CreateAppClient())
        {
            await setup.RegisterAsync(email);
        }

        using var client = factory.CreateAppClient();
        var wrongPassword = await client.LoginAsync(email, "yanlis-parola-999");
        var unknownEmail = await client.LoginAsync(NewEmail(), "yanlis-parola-999");

        Assert.Equal(HttpStatusCode.OK, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.OK, unknownEmail.StatusCode);
        var a = (await client.ParseAsync(wrongPassword)).QuerySelector(".alert-error")!.TextContent.Trim();
        var b = (await client.ParseAsync(unknownEmail)).QuerySelector(".alert-error")!.TextContent.Trim();
        Assert.Equal(LoginModel.GenericError, a);
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task Account_is_locked_after_repeated_failures_without_revealing_it()
    {
        var email = NewEmail();
        using (var setup = factory.CreateAppClient())
        {
            await setup.RegisterAsync(email);
        }

        using var client = factory.CreateAppClient();
        for (var i = 0; i < 5; i++)
        {
            await client.LoginAsync(email, "yanlis-parola-999");
        }

        // Doğru parola bile kilit süresince reddedilir; mesaj yine genel.
        var response = await client.LoginAsync(email);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(LoginModel.GenericError, (await client.ParseAsync(response)).QuerySelector(".alert-error")!.TextContent.Trim());
    }

    [Fact]
    public async Task Logout_requires_post_and_invalidates_the_server_side_session()
    {
        var email = NewEmail();
        using var client = factory.CreateAppClient();
        await client.RegisterAsync(email);

        // GET ile çıkış yapılamaz (durum değiştiren işlem değil).
        var get = await client.Http.GetAsync("/Account/Logout");
        get.AssertRedirectTo("/");
        Assert.Equal(HttpStatusCode.OK, (await client.Http.GetAsync("/Library")).StatusCode);

        // Çıkış öncesi oturum çerezini kopyala.
        using var raw = factory.CreateRawClient();
        var loginPage = await raw.GetAsync("/Account/Login");
        var antiforgeryCookie = CookieHeader(loginPage);
        var token = await ExtractTokenAsync(loginPage);
        var loginResponse = await raw.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Headers = { { "Cookie", antiforgeryCookie } },
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Email"] = email,
                ["Input.Password"] = AppClient.DefaultPassword,
                ["__RequestVerificationToken"] = token
            })
        });
        loginResponse.AssertRedirectTo("/Library");
        var sessionCookie = CookieHeader(loginResponse);

        Assert.Equal(HttpStatusCode.OK, (await GetWithCookie(raw, "/Library", sessionCookie)).StatusCode);

        // Aynı oturumla çıkış yap.
        var bothCookies = sessionCookie + "; " + antiforgeryCookie;
        var settings = await GetWithCookie(raw, "/Account/Settings", bothCookies);
        var settingsToken = await ExtractTokenAsync(settings);
        var logout = await raw.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/Account/Logout")
        {
            Headers = { { "Cookie", bothCookies } },
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = settingsToken })
        });
        logout.AssertRedirectTo("/Account/Login");

        // Eski çerez yeniden kullanılırsa artık geçersizdir.
        (await GetWithCookie(raw, "/Library", sessionCookie)).AssertRedirectToLogin();
    }

    [Fact]
    public async Task Post_without_antiforgery_token_is_rejected()
    {
        using var client = factory.CreateAppClient();
        var email = NewEmail();
        await client.RegisterAsync(email);

        var response = await client.Http.PostAsync("/Library/New", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Title"] = "CSRF denemesi",
            ["Input.PlatformId"] = "1",
            ["Input.Status"] = "NotStarted"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("/Library")]
    [InlineData("/Library/New")]
    [InlineData("/Stats")]
    [InlineData("/Account/Settings")]
    [InlineData("/Account/Export")]
    [InlineData("/Account/Delete")]
    public async Task Protected_pages_redirect_anonymous_users_to_login(string url)
    {
        using var client = factory.CreateAppClient();
        (await client.Http.GetAsync(url)).AssertRedirectToLogin();
    }

    [Fact]
    public async Task Protected_post_endpoints_reject_anonymous_users()
    {
        using var client = factory.CreateAppClient();
        var token = await client.GetTokenAsync("/Account/Login");
        var response = await client.Http.PostAsync($"/Library/{Guid.NewGuid()}/Delete",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        response.AssertRedirectToLogin();
    }

    [Fact]
    public async Task Session_cookie_is_http_only_and_same_site_lax()
    {
        var email = NewEmail();
        using (var setup = factory.CreateAppClient())
        {
            await setup.RegisterAsync(email);
        }

        using var raw = factory.CreateRawClient();
        var page = await raw.GetAsync("/Account/Login");
        var token = await ExtractTokenAsync(page);
        var response = await raw.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/Account/Login")
        {
            Headers = { { "Cookie", CookieHeader(page) } },
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Input.Email"] = email,
                ["Input.Password"] = AppClient.DefaultPassword,
                ["__RequestVerificationToken"] = token
            })
        });

        var setCookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("gv_session=", StringComparison.Ordinal));
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    internal static string CookieHeader(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join("; ", values.Select(v => v.Split(';')[0]).Where(v => !v.EndsWith('=')))
            : string.Empty;

    internal static async Task<string> ExtractTokenAsync(HttpResponseMessage response)
    {
        var doc = new AngleSharp.Html.Parser.HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync());
        return doc.QuerySelector("input[name=__RequestVerificationToken]")!.GetAttribute("value")!;
    }

    internal static Task<HttpResponseMessage> GetWithCookie(HttpClient client, string url, string cookie) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Get, url) { Headers = { { "Cookie", cookie } } });
}

