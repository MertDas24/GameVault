using System.Text.RegularExpressions;
using GameVault.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace GameVault.Tests;

/// <summary>
/// Uygulamadaki tüm endpoint'leri tarar. Elle yazılmış bir sayfa listesine dayanmaz; yeni eklenen bir
/// sayfa yanlışlıkla herkese açık kalırsa ya da korumasız bırakılırsa bu testler başarısız olur.
/// </summary>
public sealed partial class AuthorizationCoverageTests(GameVaultFactory factory) : IClassFixture<GameVaultFactory>
{
    /// <summary>
    /// Uygulamanın herkese açık yüzeyinin tamamı. Buraya bir adres eklemek bilinçli bir karar olmalıdır.
    /// </summary>
    private static readonly string[] PublicRoutes =
    [
        "",
        "Index",
        "Privacy",
        "Error",
        "Account/Login",
        "Account/Logout",
        "Account/Register",
        "Account/ConfirmEmail",
        "Account/ResendConfirmation",
        "Account/ForgotPassword",
        "Account/ResetPassword",
        "health",
        "health/live"
    ];

    private IReadOnlyList<(string Route, bool Anonymous)> Endpoints()
    {
        var source = factory.Services.GetRequiredService<EndpointDataSource>();
        return source.Endpoints
            .OfType<RouteEndpoint>()
            .Select(e => (Route: e.RoutePattern.RawText?.Trim('/') ?? string.Empty,
                          Anonymous: e.Metadata.GetMetadata<IAllowAnonymous>() is not null))
            .Distinct()
            .OrderBy(e => e.Route, StringComparer.Ordinal)
            .ToList();
    }

    [Fact]
    public async Task Fallback_policy_requires_an_authenticated_user()
    {
        var provider = factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var fallback = await provider.GetFallbackPolicyAsync();

        Assert.NotNull(fallback);
        Assert.Contains(fallback.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public void Only_the_expected_routes_allow_anonymous_access()
    {
        var anonymous = Endpoints().Where(e => e.Anonymous).Select(e => e.Route).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(PublicRoutes.Order(StringComparer.Ordinal), anonymous.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Every_protected_route_redirects_anonymous_users_to_login()
    {
        var protectedRoutes = Endpoints().Where(e => !e.Anonymous).Select(e => e.Route).ToList();
        Assert.NotEmpty(protectedRoutes);
        Assert.Contains("Library", protectedRoutes);

        using var client = factory.CreateAppClient();
        var failures = new List<string>();
        foreach (var route in protectedRoutes)
        {
            // Rota parametrelerini ({id:guid} gibi) geçerli bir değerle doldur.
            var url = "/" + RouteParameter().Replace(route, _ => Guid.NewGuid().ToString());
            using var response = await client.Http.GetAsync(url);
            var location = response.Headers.Location?.OriginalString ?? string.Empty;
            if ((int)response.StatusCode != 302 || !location.Contains("/Account/Login", StringComparison.Ordinal))
            {
                failures.Add($"{url} → {(int)response.StatusCode} {location}");
            }
        }

        Assert.True(failures.Count == 0, "Anonim erişime açık korumalı adresler:\n" + string.Join('\n', failures));
    }

    [GeneratedRegex(@"\{[^}]+\}")]
    private static partial Regex RouteParameter();
}
