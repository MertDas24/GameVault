using System.ComponentModel.DataAnnotations;

namespace GameVault.Web.Pages.Account;

public sealed class LoginInput
{
    [Display(Name = "E-posta")]
    [Required(ErrorMessage = "E-posta adresini girin.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
    [StringLength(256, ErrorMessage = "E-posta en fazla {1} karakter olabilir.")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Parola")]
    [Required(ErrorMessage = "Parolanı gir.")]
    [StringLength(256, ErrorMessage = "Parola çok uzun.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Beni hatırla")]
    public bool RememberMe { get; set; }
}

public sealed class RegisterInput
{
    [Display(Name = "E-posta")]
    [Required(ErrorMessage = "E-posta adresini girin.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
    [StringLength(256, ErrorMessage = "E-posta en fazla {1} karakter olabilir.")]
    public string Email { get; set; } = string.Empty;

    [Display(Name = "Görünen ad")]
    [StringLength(50, ErrorMessage = "Görünen ad en fazla {1} karakter olabilir.")]
    public string? DisplayName { get; set; }

    [Display(Name = "Parola")]
    [Required(ErrorMessage = "Bir parola belirle.")]
    [StringLength(128, MinimumLength = 10, ErrorMessage = "Parola {2} ile {1} karakter arasında olmalı.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Parola (tekrar)")]
    [Required(ErrorMessage = "Parolayı tekrar gir.")]
    [Compare(nameof(Password), ErrorMessage = "Parolalar eşleşmiyor.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class EmailInput
{
    [Display(Name = "E-posta")]
    [Required(ErrorMessage = "E-posta adresini girin.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi girin.")]
    [StringLength(256, ErrorMessage = "E-posta en fazla {1} karakter olabilir.")]
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordInput
{
    [Required]
    [StringLength(64)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [StringLength(2048)]
    public string Token { get; set; } = string.Empty;

    [Display(Name = "Yeni parola")]
    [Required(ErrorMessage = "Yeni bir parola belirle.")]
    [StringLength(128, MinimumLength = 10, ErrorMessage = "Parola {2} ile {1} karakter arasında olmalı.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "Yeni parola (tekrar)")]
    [Required(ErrorMessage = "Parolayı tekrar gir.")]
    [Compare(nameof(Password), ErrorMessage = "Parolalar eşleşmiyor.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class ProfileInput
{
    [Display(Name = "Görünen ad")]
    [StringLength(50, ErrorMessage = "Görünen ad en fazla {1} karakter olabilir.")]
    public string? DisplayName { get; set; }
}

public sealed class ChangePasswordInput
{
    [Display(Name = "Mevcut parola")]
    [Required(ErrorMessage = "Mevcut parolanı gir.")]
    [StringLength(256)]
    [DataType(DataType.Password)]
    public string CurrentPassword { get; set; } = string.Empty;

    [Display(Name = "Yeni parola")]
    [Required(ErrorMessage = "Yeni bir parola belirle.")]
    [StringLength(128, MinimumLength = 10, ErrorMessage = "Parola {2} ile {1} karakter arasında olmalı.")]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = string.Empty;

    [Display(Name = "Yeni parola (tekrar)")]
    [Required(ErrorMessage = "Parolayı tekrar gir.")]
    [Compare(nameof(NewPassword), ErrorMessage = "Parolalar eşleşmiyor.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed class DeleteAccountInput
{
    [Display(Name = "Parola")]
    [Required(ErrorMessage = "Kimliğini doğrulamak için parolanı gir.")]
    [StringLength(256)]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Display(Name = "E-posta adresin")]
    [Required(ErrorMessage = "Onay için e-posta adresini yaz.")]
    [StringLength(256)]
    public string ConfirmEmail { get; set; } = string.Empty;

    [Display(Name = "Bu işlemin geri alınamayacağını anlıyorum")]
    [Range(typeof(bool), "true", "true", ErrorMessage = "Devam etmek için bu kutuyu işaretle.")]
    public bool Acknowledge { get; set; }
}

public sealed class TwoFactorCodeInput
{
    [Display(Name = "Doğrulama kodu")]
    [Required(ErrorMessage = "Doğrulayıcı uygulamandaki 6 haneli kodu gir.")]
    [StringLength(16, ErrorMessage = "Kod çok uzun.")]
    public string Code { get; set; } = string.Empty;

    /// <summary>Girişte "Beni hatırla" seçimi ikinci adıma taşınır.</summary>
    public bool RememberMe { get; set; }
}

public sealed class RecoveryCodeInput
{
    [Display(Name = "Kurtarma kodu")]
    [Required(ErrorMessage = "Kurtarma kodlarından birini gir.")]
    [StringLength(32, ErrorMessage = "Kod çok uzun.")]
    public string Code { get; set; } = string.Empty;
}

public sealed class EnableTwoFactorInput
{
    [Display(Name = "Doğrulama kodu")]
    [Required(ErrorMessage = "Uygulamada görünen 6 haneli kodu gir.")]
    [StringLength(16, ErrorMessage = "Kod çok uzun.")]
    public string Code { get; set; } = string.Empty;

    [Display(Name = "Mevcut parola")]
    [Required(ErrorMessage = "Parolanı gir.")]
    [StringLength(256, ErrorMessage = "Parola çok uzun.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
}

public sealed class PasswordConfirmInput
{
    [Display(Name = "Mevcut parola")]
    [Required(ErrorMessage = "Parolanı gir.")]
    [StringLength(256, ErrorMessage = "Parola çok uzun.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
}
