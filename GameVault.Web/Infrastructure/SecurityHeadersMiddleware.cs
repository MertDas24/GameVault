using GameVault.Business.Covers;

namespace GameVault.Web.Infrastructure;

/// <summary>
/// Tüm yanıtlara güvenlik başlıklarını ekler. CSP satır içi betik/stil kabul etmez;
/// görseller yalnızca aynı kaynaktan ve yapılandırılmış kapak alan adlarından yüklenir.
/// Dinamik (kullanıcıya özel olabilecek) yanıtlar paylaşılan önbelleklerde saklanmaz.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _csp;

    public SecurityHeadersMiddleware(RequestDelegate next, CoverPolicy covers)
    {
        _next = next;
        var imgSources = string.Join(' ', covers.AllowedHosts.Select(h => "https://" + h).Prepend("'self'"));
        _csp = string.Join("; ",
            "default-src 'self'",
            $"img-src {imgSources}",
            "script-src 'self'",
            "style-src 'self'",
            "font-src 'self'",
            "connect-src 'self'",
            "object-src 'none'",
            "base-uri 'none'",
            "form-action 'self'",
            "frame-ancestors 'none'");
    }

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var (ctx, csp) = ((HttpContext, string))state;
            var headers = ctx.Response.Headers;
            headers.ContentSecurityPolicy = csp;
            headers.XContentTypeOptions = "nosniff";
            headers.XFrameOptions = "DENY";
            headers["Referrer-Policy"] = "same-origin";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()";
            headers["Cross-Origin-Opener-Policy"] = "same-origin";
            headers["Cross-Origin-Resource-Policy"] = "same-origin";

            // Statik dosyalar kendi Cache-Control değerini belirler; geri kalan her şey saklanmaz.
            if (string.IsNullOrEmpty(headers.CacheControl))
            {
                headers.CacheControl = "no-store";
                headers.Pragma = "no-cache";
            }

            return Task.CompletedTask;
        }, (context, _csp));

        return _next(context);
    }
}
