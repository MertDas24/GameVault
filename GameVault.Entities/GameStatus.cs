namespace GameVault.Entities;

/// <summary>
/// Oynama durumu. Sayısal değerler veritabanında saklanır; mevcut değerler (0-3)
/// eski şemayla uyumlu kalacak şekilde korunmuştur, yeni değerler sona eklenir.
/// </summary>
public enum GameStatus
{
    NotStarted = 0, // eski adı: Backlog
    Playing = 1,
    Completed = 2,
    Dropped = 3,
    OnHold = 4,
    Wishlist = 5
}
