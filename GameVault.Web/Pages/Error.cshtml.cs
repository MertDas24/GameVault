using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameVault.Web.Pages;

/// <summary>
/// Hata sayfası. Ayrıntı (stack trace, SQL, istisna mesajı) göstermez; yalnızca durum koduna
/// göre genel bir açıklama ve geri dönüş bağlantısı sunar.
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]
public sealed class ErrorModel : PageModel
{
    public int Code { get; private set; } = 500;

    public string Heading => Code switch
    {
        400 => "İstek işlenemedi",
        404 => "Sayfa bulunamadı",
        405 => "Bu işlem desteklenmiyor",
        413 => "İstek çok büyük",
        429 => "Çok fazla istek",
        _ => "Bir şeyler ters gitti"
    };

    public string Message => Code switch
    {
        400 => "Form süresi dolmuş ya da istek geçersiz olabilir. Sayfayı yenileyip tekrar dene.",
        404 => "Aradığın sayfa ya da kayıt bulunamadı. Silinmiş olabilir veya erişim iznin olmayabilir.",
        405 => "Bu adres bu şekilde kullanılamaz.",
        413 => "Gönderdiğin veri izin verilen boyutu aşıyor.",
        429 => "Kısa sürede çok fazla istek yapıldı. Biraz bekleyip tekrar dene.",
        _ => "Beklenmeyen bir hata oluştu. Sorun devam ederse daha sonra tekrar dene."
    };

    public void OnGet(int? code) => SetCode(code);

    public void OnPost(int? code) => SetCode(code);

    private void SetCode(int? code)
    {
        Code = code is >= 400 and < 600 ? code.Value : 500;
        // Yeniden yürütmede yanıt kodu zaten ayarlanmıştır; doğrudan ziyarette 200 kalır.
    }
}
