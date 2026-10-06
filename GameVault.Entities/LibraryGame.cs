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

    /// <summary>Kullanıcının bu oyunu eklediği koleksiyonlar (yalnızca aynı kullanıcıya ait olanlar).</summary>
    public ICollection<GameCollection> Collections { get; set; } = new List<GameCollection>();

    public GameStatus Status { get; set; } = GameStatus.NotStarted;
    public int? Rating { get; set; }
    public int PlaytimeHours { get; set; }
    public bool IsFavorite { get; set; }
    public string? Notes { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? CompletionDate { get; set; }

    /// <summary>Oyunun çıkış tarihi (isteğe bağlı; RAWG'dan doldurulabilir).</summary>
    public DateOnly? ReleaseDate { get; set; }

    /// <summary>
    /// Oyunu bitirmek için tahmini süre (saat). Kullanıcının oynadığı süreden (PlaytimeHours) farklıdır;
    /// RAWG'ın ortalama oynanma süresinden doldurulabilir. "Ne oynasam?" filtresinde kullanılır.
    /// </summary>
    public int? EstimatedHours { get; set; }

    /// <summary>
    /// Oyun RAWG aramasından eklendiyse RAWG'daki kimliği. Yalnızca oluştururken atanır;
    /// aynı kullanıcı aynı RAWG oyununu ikinci kez ekleyemez (benzersiz filtreli indeks).
    /// </summary>
    public int? RawgId { get; set; }

    /// <summary>Eklenme zamanı (UTC).</summary>
    public DateTime DateAdded { get; set; }

    /// <summary>Son güncelleme zamanı (UTC).</summary>
    public DateTime UpdatedAtUtc { get; set; }
}
