namespace GameVault.Entities;

/// <summary>
/// Bir kullanıcının kütüphanesindeki tek bir oyun. Her kayıt yalnızca sahibine aittir;
/// kullanıcılar arasında paylaşılan bir oyun kataloğu yoktur.
/// </summary>
public class LibraryGame
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CoverImageUrl { get; set; }

    public int PlatformId { get; set; }
    public Platform Platform { get; set; } = null!;

    public ICollection<Genre> Genres { get; set; } = new List<Genre>();

    public GameStatus Status { get; set; } = GameStatus.NotStarted;
    public int? Rating { get; set; }
    public int PlaytimeHours { get; set; }
    public bool IsFavorite { get; set; }
    public string? Notes { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? CompletionDate { get; set; }

    /// <summary>Eklenme zamanı (UTC).</summary>
    public DateTime DateAdded { get; set; }

    /// <summary>Son güncelleme zamanı (UTC).</summary>
    public DateTime UpdatedAtUtc { get; set; }
}
