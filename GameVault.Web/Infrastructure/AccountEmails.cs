using System.Text;
using GameVault.Business.Email;
using GameVault.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace GameVault.Web.Infrastructure;

/// <summary>
/// Hesapla ilgili e-postaları hazırlar ve kalıcı outbox'a yazar. Bağlantılar istekteki Host başlığından
/// değil, yapılandırılmış genel adresten kurulur. Aynı kullanıcıya aynı türde e-posta kısa aralıkla
/// iki kez kuyruğa girmez (DedupKey).
/// </summary>
public sealed class AccountEmails(UserManager<ApplicationUser> users, EmailOutbox outbox, IOptions<AppOptions> app)
{
    private static readonly TimeSpan DedupWindow = TimeSpan.FromMinutes(2);

    private string BaseUrl => app.Value.PublicBaseUrl.TrimEnd('/');

    public async Task SendConfirmationAsync(ApplicationUser user, CancellationToken ct)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        var link = $"{BaseUrl}/Account/ConfirmEmail?uid={user.Id:N}&token={Encode(token)}";
        await outbox.EnqueueAsync(
            new EmailMessage(user.Email!, "Game Vault: e-posta adresini doğrula", $"""
                Merhaba,

                Game Vault hesabını etkinleştirmek için aşağıdaki bağlantıyı 24 saat içinde aç ve
                "E-postamı doğrula" düğmesine bas:

                {link}

                Bu hesabı sen oluşturmadıysan bu e-postayı yok sayabilirsin; doğrulanmamış hesaplar
                giriş yapamaz.
                """),
            OutboxKinds.ConfirmEmail, user.Id, EmailConfirmationTokenProvider.Lifespan,
            $"{OutboxKinds.ConfirmEmail}:{user.Id:N}:{outbox.Bucket(DedupWindow)}", ct);
    }

    /// <summary>Kayıtlı bir adresle yeniden kayıt denendiğinde gerçek sahibine bilgi verir (yanıt aynı kalır).</summary>
    public async Task SendAccountExistsAsync(ApplicationUser user, CancellationToken ct) =>
        await outbox.EnqueueAsync(
            new EmailMessage(user.Email!, "Game Vault: bu adresle zaten bir hesabın var", $"""
                Merhaba,

                Bu e-posta adresiyle yeni bir Game Vault hesabı oluşturulmak istendi, ancak adres zaten
                kayıtlı. Hesabına giriş yapabilir ya da parolanı unuttuysan sıfırlayabilirsin:

                {BaseUrl}/Account/Login
                {BaseUrl}/Account/ForgotPassword

                Bu isteği sen yapmadıysan bir şey yapmana gerek yok.
                """),
            OutboxKinds.AccountExists, user.Id, TimeSpan.FromHours(24),
            $"{OutboxKinds.AccountExists}:{user.Id:N}:{outbox.Bucket(TimeSpan.FromHours(1))}", ct);

    public async Task SendPasswordResetAsync(ApplicationUser user, CancellationToken ct)
    {
        var token = await users.GeneratePasswordResetTokenAsync(user);
        var link = $"{BaseUrl}/Account/ResetPassword?uid={user.Id:N}&token={Encode(token)}";
        await outbox.EnqueueAsync(
            new EmailMessage(user.Email!, "Game Vault parola sıfırlama", $"""
                Merhaba,

                Game Vault hesabın için parola sıfırlama isteği aldık. Yeni parola belirlemek için
                aşağıdaki bağlantıyı 30 dakika içinde aç:

                {link}

                Bağlantı yalnızca bir kez kullanılabilir. Parola değiştiğinde tüm cihazlardaki
                oturumların kapatılır. Bu isteği sen yapmadıysan bu e-postayı yok sayabilirsin.
                """),
            OutboxKinds.ResetPassword, user.Id, TimeSpan.FromMinutes(30),
            $"{OutboxKinds.ResetPassword}:{user.Id:N}:{outbox.Bucket(DedupWindow)}", ct);
    }

    public static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    public static bool TryDecode(string encoded, out string token)
    {
        try
        {
            token = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(encoded));
            return true;
        }
        catch (FormatException)
        {
            token = string.Empty;
            return false;
        }
    }
}
