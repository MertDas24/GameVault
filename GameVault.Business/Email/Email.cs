using System.ComponentModel.DataAnnotations;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace GameVault.Business.Email;

/// <param name="MessageId">SMTP Message-ID; aynı outbox işi yeniden gönderilirse alıcı sunucular yinelenenleri tanıyabilir.</param>
public sealed record EmailMessage(string To, string Subject, string TextBody, string? MessageId = null);

public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

public enum SmtpSecurity
{
    None,
    StartTls,
    SslOnConnect
}

public sealed class EmailOptions
{
    public const string SectionName = "Email";

    [Required(ErrorMessage = "Email:Host tanımlı değil.")]
    public string Host { get; set; } = string.Empty;

    [Range(1, 65535)]
    public int Port { get; set; } = 587;

    /// <summary>Üretimde None kabul edilmez (bkz. başlangıç doğrulaması).</summary>
    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    public string? Username { get; set; }
    public string? Password { get; set; }

    [Required(ErrorMessage = "Email:FromAddress tanımlı değil.")]
    [EmailAddress]
    public string FromAddress { get; set; } = string.Empty;

    public string FromName { get; set; } = "Game Vault";

    /// <summary>
    /// Development ortamında yalnızca yerel SMTP'ye (Mailpit vb.) izin verilir; gerçek alıcılara yanlışlıkla
    /// e-posta gitmesin. Bilerek uzak bir SMTP kullanmak için true yapın.
    /// </summary>
    public bool AllowNonLocalSmtpInDevelopment { get; set; }
}

public sealed class SmtpEmailSender(IOptions<EmailOptions> options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        var o = options.Value;
        using var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(o.FromName, o.FromAddress));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        if (message.MessageId is not null)
        {
            mime.MessageId = message.MessageId;
        }

        mime.Body = new TextPart("plain") { Text = message.TextBody };

        using var client = new SmtpClient();
        var socketOptions = o.Security switch
        {
            SmtpSecurity.None => SecureSocketOptions.None,
            SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
            _ => SecureSocketOptions.StartTls
        };
        await client.ConnectAsync(o.Host, o.Port, socketOptions, ct);
        if (!string.IsNullOrEmpty(o.Username))
        {
            await client.AuthenticateAsync(o.Username, o.Password ?? string.Empty, ct);
        }

        await client.SendAsync(mime, ct);
        await client.DisconnectAsync(quit: true, ct);
    }
}
