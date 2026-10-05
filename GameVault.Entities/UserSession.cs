namespace GameVault.Entities;

/// <summary>
/// Sunucu tarafında saklanan oturum. Tarayıcı çerezi yalnızca bu kaydın anahtarını
/// (Data Protection ile korunmuş olarak) taşır; kayıt silindiğinde oturum hemen geçersiz olur.
/// </summary>
public class UserSession
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public byte[] Ticket { get; set; } = [];
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
