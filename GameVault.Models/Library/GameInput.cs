using System.ComponentModel.DataAnnotations;
using GameVault.Entities;

namespace GameVault.Models.Library;

/// <summary>
/// Oyun ekleme/düzenleme formunun kabul ettiği alanların tamamı. Varlığa doğrudan
/// bağlanmaz; sahiplik (UserId), kimlik ve tarih alanları burada bulunmadığından
/// istemci bunları değiştiremez (mass assignment koruması).
/// </summary>
public sealed class GameInput
{
    public const int MaxGenres = 5;
    public const int TitleMaxLength = 200;
    public const int CoverUrlMaxLength = 500;

    [Display(Name = "Oyun adı")]
    [Required(ErrorMessage = "Oyun adı zorunludur.")]
    [StringLength(TitleMaxLength, ErrorMessage = "Oyun adı en fazla {1} karakter olabilir.")]
    public string Title { get; set; } = string.Empty;

    [Display(Name = "Açıklama")]
    [StringLength(4000, ErrorMessage = "Açıklama en fazla {1} karakter olabilir.")]
    public string? Description { get; set; }

    /// <summary>"none", "builtin:&lt;anahtar&gt;" veya "url".</summary>
    [Display(Name = "Kapak")]
    [StringLength(64, ErrorMessage = "Geçersiz kapak seçimi.")]
    public string CoverChoice { get; set; } = CoverChoices.None;

    [Display(Name = "Kapak görseli adresi")]
    [StringLength(CoverUrlMaxLength, ErrorMessage = "Adres en fazla {1} karakter olabilir.")]
    public string? CoverUrl { get; set; }

    [Display(Name = "Platform")]
    [Required(ErrorMessage = "Bir platform seçin.")]
    public int? PlatformId { get; set; }

    [Display(Name = "Türler")]
    [MaxLength(MaxGenres, ErrorMessage = "En fazla 5 tür seçebilirsiniz.")]
    public List<int> GenreIds { get; set; } = [];

    [Display(Name = "Durum")]
    [Required(ErrorMessage = "Bir durum seçin.")]
    [EnumDataType(typeof(GameStatus), ErrorMessage = "Geçersiz durum.")]
    public GameStatus? Status { get; set; } = GameStatus.NotStarted;

    [Display(Name = "Oynama süresi (saat)")]
    [Range(0, 100_000, ErrorMessage = "Oynama süresi 0 ile 100000 saat arasında olmalı.")]
    public int PlaytimeHours { get; set; }

    [Display(Name = "Kişisel puan")]
    [Range(1, 10, ErrorMessage = "Puan 1 ile 10 arasında olmalı.")]
    public int? Rating { get; set; }

    [Display(Name = "Notlar")]
    [StringLength(4000, ErrorMessage = "Notlar en fazla {1} karakter olabilir.")]
    public string? Notes { get; set; }

    [Display(Name = "Favori")]
    public bool IsFavorite { get; set; }

    [Display(Name = "Başlama tarihi")]
    public DateOnly? StartDate { get; set; }

    [Display(Name = "Bitirme tarihi")]
    public DateOnly? CompletionDate { get; set; }

    [Display(Name = "Çıkış tarihi")]
    public DateOnly? ReleaseDate { get; set; }

    public const int MaxEstimatedHours = 1000;

    [Display(Name = "Tahmini süre (saat)")]
    [Range(1, MaxEstimatedHours, ErrorMessage = "Tahmini süre 1 ile 1000 saat arasında olmalı.")]
    public int? EstimatedHours { get; set; }

    /// <summary>
    /// RAWG aramasından seçildiyse RAWG kimliği. Yalnızca yeni oyun oluştururken dikkate alınır;
    /// güncellemede yok sayılır.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "Geçersiz RAWG kimliği.")]
    public int? RawgId { get; set; }
}

public static class CoverChoices
{
    public const string None = "none";
    public const string Url = "url";
    public const string BuiltInPrefix = "builtin:";
}
