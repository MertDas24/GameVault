using System.Net;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace GameVault.Tests.Infrastructure;

/// <summary>
/// Razor Pages'i tarayıcı gibi kullanan test istemcisi: sayfayı açar, formdaki antiforgery
/// tokenını alır, formu gönderir. Yönlendirmeler otomatik izlenmez (Location doğrulanabilsin).
/// </summary>
public sealed partial class AppClient(HttpClient http, CapturingEmailSender emails) : IDisposable
{
    public const string DefaultPassword = "dogru-parola-123";
    private static readonly HtmlParser Parser = new();

    public HttpClient Http => http;

    public async Task<(HttpResponseMessage Response, IDocument Document)> GetPageAsync(string url)
    {
        var response = await http.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();
        return (response, Parser.ParseDocument(html));
    }

    public async Task<string> GetTokenAsync(string pageUrl, string? formSelector = null)
    {
        var (response, doc) = await GetPageAsync(pageUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var scope = formSelector is null ? (IParentNode)doc : doc.QuerySelector(formSelector) ?? throw new InvalidOperationException($"Form yok: {formSelector}");
        var input = scope.QuerySelector("input[name=__RequestVerificationToken]")
            ?? throw new InvalidOperationException($"{pageUrl} sayfasında antiforgery tokenı yok.");
        return input.GetAttribute("value")!;
    }

    /// <summary>tokenPage sayfasından token alıp postUrl adresine form gönderir.</summary>
    public async Task<HttpResponseMessage> PostFormAsync(
        string postUrl, IEnumerable<KeyValuePair<string, string>> fields, string? tokenPage = null, string? formSelector = null)
    {
        var token = await GetTokenAsync(tokenPage ?? postUrl, formSelector);
        var all = fields.Append(new("__RequestVerificationToken", token)).ToList();
        return await http.PostAsync(postUrl, new FormUrlEncodedContent(all));
    }

    /// <summary>Dosya yükleme formu (multipart/form-data) gönderir; token aynı sayfadan alınır.</summary>
    public async Task<HttpResponseMessage> PostFileAsync(string url, string fileField, byte[] content, string fileName, params (string Key, string Value)[] fields)
    {
        var token = await GetTokenAsync(url);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(token), "__RequestVerificationToken");
        foreach (var (key, value) in fields)
        {
            form.Add(new StringContent(value), key);
        }

        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        form.Add(file, fileField, fileName);
        return await http.PostAsync(url, form);
    }

    public async Task<IDocument> ParseAsync(HttpResponseMessage response) =>
        Parser.ParseDocument(await response.Content.ReadAsStringAsync());

    [System.Text.RegularExpressions.GeneratedRegex(@"http://localhost(/Account/ConfirmEmail\?uid=[0-9a-f]{32}&token=[A-Za-z0-9_\-]+)")]
    private static partial System.Text.RegularExpressions.Regex ConfirmLink();

    /// <summary>Kayıt → e-postadaki doğrulama bağlantısı → giriş. Giriş yanıtını döndürür.</summary>
    public async Task<HttpResponseMessage> RegisterAsync(string email, string password = DefaultPassword, string? displayName = null)
    {
        var submitted = await SubmitRegistrationAsync(email, password, displayName);
        submitted.AssertRedirectTo("/Account/Register?submitted=True");
        var confirmed = await ConfirmEmailAsync(email);
        confirmed.AssertRedirectTo("/Account/Login");
        return await LoginAsync(email, password);
    }

    /// <summary>E-postadaki en son doğrulama bağlantısının yolu.</summary>
    public async Task<string> GetConfirmationLinkAsync(string email)
    {
        await emails.WaitForAsync(email, "doğrula");
        var message = emails.Messages.Last(m => string.Equals(m.To, email, StringComparison.OrdinalIgnoreCase) && m.Subject.Contains("doğrula", StringComparison.Ordinal));
        var match = ConfirmLink().Match(message.TextBody);
        Assert.True(match.Success, "Doğrulama bağlantısı bulunamadı.");
        return match.Groups[1].Value;
    }

    /// <summary>Doğrulama sayfasını açar ve düğmeye basar (POST).</summary>
    public async Task<HttpResponseMessage> ConfirmEmailAsync(string email, string? link = null)
    {
        link ??= await GetConfirmationLinkAsync(email);
        var (_, doc) = await GetPageAsync(link);
        var fields = new Dictionary<string, string>
        {
            ["Uid"] = doc.QuerySelector("input[name=Uid]")?.GetAttribute("value") ?? string.Empty,
            ["Token"] = doc.QuerySelector("input[name=Token]")?.GetAttribute("value") ?? string.Empty
        };
        return await PostFormAsync("/Account/ConfirmEmail", fields, tokenPage: "/Account/Login");
    }

    public async Task<HttpResponseMessage> SubmitRegistrationAsync(string email, string password = DefaultPassword, string? displayName = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password
        };
        if (displayName is not null)
        {
            fields["Input.DisplayName"] = displayName;
        }

        return await PostFormAsync("/Account/Register", fields);
    }

    public Task<HttpResponseMessage> LoginAsync(string email, string password = DefaultPassword, string? returnUrl = null) =>
        PostFormAsync("/Account/Login" + (returnUrl is null ? string.Empty : "?returnUrl=" + Uri.EscapeDataString(returnUrl)),
            new Dictionary<string, string>
            {
                ["Input.Email"] = email,
                ["Input.Password"] = password
            });

    public Task<HttpResponseMessage> LogoutAsync() =>
        PostFormAsync("/Account/Logout", [], tokenPage: "/Account/Settings", formSelector: "form[action='/Account/Logout']");

    public async Task<int> GetPlatformIdAsync(string name = "PC")
    {
        var (_, doc) = await GetPageAsync("/Library/New");
        var option = doc.QuerySelectorAll("select[name='Input.PlatformId'] option").First(o => o.TextContent.Trim() == name);
        return int.Parse(option.GetAttribute("value")!, System.Globalization.CultureInfo.InvariantCulture);
    }

    public async Task<int> GetGenreIdAsync(string name)
    {
        var (_, doc) = await GetPageAsync("/Library/New");
        var input = doc.QuerySelectorAll("input[name='Input.GenreIds']").First(i => i.ParentElement!.TextContent.Trim() == name);
        return int.Parse(input.GetAttribute("value")!, System.Globalization.CultureInfo.InvariantCulture);
    }

    public static Dictionary<string, string> GameFields(string title, int platformId, string status = "NotStarted", params (string Key, string Value)[] extra)
    {
        var fields = new Dictionary<string, string>
        {
            ["Input.Title"] = title,
            ["Input.PlatformId"] = platformId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Input.Status"] = status,
            ["Input.PlaytimeHours"] = "0",
            ["Input.CoverChoice"] = "none"
        };
        foreach (var (key, value) in extra)
        {
            fields[key] = value;
        }

        return fields;
    }

    public async Task<Guid> CreateGameAsync(string title, int platformId, string status = "NotStarted", params (string Key, string Value)[] extra)
    {
        var response = await PostFormAsync("/Library/New", GameFields(title, platformId, status, extra));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        return Guid.Parse(location.Split('/').Last());
    }

    public void Dispose() => http.Dispose();
}

public static class ResponseAssertions
{
    public static void AssertRedirectTo(this HttpResponseMessage response, string expectedPathAndQuery)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        var actual = location.IsAbsoluteUri ? location.PathAndQuery : location.OriginalString;
        Assert.Equal(expectedPathAndQuery, actual);
    }

    public static void AssertRedirectToLogin(this HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!;
        var path = location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString.Split('?')[0];
        Assert.Equal("/Account/Login", path);
    }
}
