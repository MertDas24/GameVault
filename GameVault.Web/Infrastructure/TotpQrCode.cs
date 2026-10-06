using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Html;
using QRCoder;

namespace GameVault.Web.Infrastructure;

/// <summary>
/// Doğrulayıcı uygulama kurulumu için QR kodu. QRCoder yalnızca modül matrisini üretir; SVG burada,
/// satır içi stil olmadan (CSP: style-src 'self') ve dış kaynak yüklemeden çizilir. Çıktıda kullanıcı
/// girdisi yoktur: yalnızca sayılardan oluşan bir yol (path) ve sabit öznitelikler.
/// </summary>
public static class TotpQrCode
{
    public static IHtmlContent Svg(string text, string label)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
        var matrix = data.ModuleMatrix;

        // QRCoder matrisi, okuyucuların ihtiyaç duyduğu 4 modüllük boş kenarla (quiet zone) birlikte döndürür.
        var size = matrix.Count;
        var path = new StringBuilder();
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (matrix[y][x])
                {
                    path.Append(CultureInfo.InvariantCulture, $"M{x},{y}h1v1h-1z");
                }
            }
        }

        var encodedLabel = System.Net.WebUtility.HtmlEncode(label);
        var viewBox = size.ToString(CultureInfo.InvariantCulture);
        return new HtmlString(
            $"""<svg class="qr" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {viewBox} {viewBox}" role="img" aria-label="{encodedLabel}" shape-rendering="crispEdges"><rect width="{viewBox}" height="{viewBox}" fill="#fff"/><path d="{path}" fill="#000"/></svg>""");
    }

    /// <summary>Elle girilecek anahtarı 4'lü gruplara ayırır (okunurluk için; boşluklar yok sayılır).</summary>
    public static string FormatKey(string key)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < key.Length; i += 4)
        {
            if (i > 0)
            {
                builder.Append(' ');
            }

            builder.Append(key.AsSpan(i, Math.Min(4, key.Length - i)));
        }

        return builder.ToString().ToLowerInvariant();
    }

    /// <summary>Uygulamaların okuduğu otpauth adresi (RFC 6238, SHA-1, 6 hane, 30 sn).</summary>
    public static string AuthenticatorUri(string issuer, string account, string key) =>
        string.Create(CultureInfo.InvariantCulture,
            $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(account)}?secret={key}&issuer={Uri.EscapeDataString(issuer)}&digits=6");

    /// <summary>Koddaki boşluk ve tireleri atar (kullanıcı "123 456" yazabilir).</summary>
    public static string NormalizeCode(string? code) =>
        string.Concat((code ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-'));
}
