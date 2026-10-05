namespace GameVault.Entities;

public enum OutboxStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,
    Expired = 3
}

/// <summary>
/// Kalıcı e-posta kuyruğu (transactional outbox). E-postalar önce veritabanına yazılır, arka plan
/// işçisi gönderir; uygulama yeniden başlasa da iş kaybolmaz. Gövde (bağlantı/token içerebilir)
/// Data Protection ile şifrelenmiş saklanır ve gönderim tamamlanınca silinir.
/// </summary>
public class OutboxEmail
{
    public Guid Id { get; set; }

    /// <summary>E-posta türü (ör. "confirm-email", "reset-password"); loglanabilir, kişisel veri değildir.</summary>
    public string Kind { get; set; } = string.Empty;

    public Guid? UserId { get; set; }

    /// <summary>Alıcı; gönderim tamamlanınca boşaltılır.</summary>
    public string? Recipient { get; set; }

    public string? Subject { get; set; }

    /// <summary>Data Protection ile şifrelenmiş gövde; gönderim tamamlanınca silinir.</summary>
    public byte[]? ProtectedBody { get; set; }

    /// <summary>Aynı işin kısa sürede iki kez kuyruğa girmesini engelleyen anahtar (isteğe bağlı, benzersiz).</summary>
    public string? DedupKey { get; set; }

    public OutboxStatus Status { get; set; }
    public int Attempts { get; set; }

    /// <summary>Son hatanın türü (ör. "SmtpCommandException"); mesaj ya da alıcı içermez.</summary>
    public string? LastError { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? LockedUntilUtc { get; set; }

    /// <summary>Bu zamandan sonra gönderilmez (içindeki bağlantının süresi dolmuştur).</summary>
    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }
}
