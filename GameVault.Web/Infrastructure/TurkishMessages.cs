using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;

namespace GameVault.Web.Infrastructure;

public static class TurkishModelBindingMessages
{
    public static void Apply(DefaultModelBindingMessageProvider p)
    {
        p.SetAttemptedValueIsInvalidAccessor((_, field) => $"{field} için girilen değer geçersiz.");
        p.SetMissingBindRequiredValueAccessor(field => $"{field} alanı zorunludur.");
        p.SetMissingKeyOrValueAccessor(() => "Değer zorunludur.");
        p.SetMissingRequestBodyRequiredValueAccessor(() => "İstek gövdesi boş olamaz.");
        p.SetNonPropertyAttemptedValueIsInvalidAccessor(_ => "Girilen değer geçersiz.");
        p.SetNonPropertyUnknownValueIsInvalidAccessor(() => "Girilen değer geçersiz.");
        p.SetNonPropertyValueMustBeANumberAccessor(() => "Bir sayı girin.");
        p.SetUnknownValueIsInvalidAccessor(field => $"{field} için girilen değer geçersiz.");
        p.SetValueIsInvalidAccessor(_ => "Girilen değer geçersiz.");
        p.SetValueMustBeANumberAccessor(field => $"{field} bir sayı olmalı.");
        p.SetValueMustNotBeNullAccessor(field => $"{field} alanı zorunludur.");
    }
}

/// <summary>Identity hata mesajlarının Türkçe karşılıkları (kullanıcıya gösterilenler).</summary>
public sealed class TurkishIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"Parola en az {length} karakter olmalı." };

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        new() { Code = nameof(PasswordRequiresUniqueChars), Description = $"Parola en az {uniqueChars} farklı karakter içermeli." };

    public override IdentityError PasswordMismatch() =>
        new() { Code = nameof(PasswordMismatch), Description = "Parola hatalı." };

    public override IdentityError InvalidToken() =>
        new() { Code = nameof(InvalidToken), Description = "Bağlantı geçersiz ya da süresi dolmuş." };

    public override IdentityError DuplicateEmail(string email) =>
        new() { Code = nameof(DuplicateEmail), Description = "Bu e-posta adresiyle kayıt oluşturulamıyor." };

    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateUserName), Description = "Bu e-posta adresiyle kayıt oluşturulamıyor." };

    public override IdentityError InvalidEmail(string? email) =>
        new() { Code = nameof(InvalidEmail), Description = "Geçerli bir e-posta adresi girin." };

    public override IdentityError InvalidUserName(string? userName) =>
        new() { Code = nameof(InvalidUserName), Description = "E-posta adresi desteklenmeyen karakterler içeriyor." };

    public override IdentityError DefaultError() =>
        new() { Code = nameof(DefaultError), Description = "İşlem tamamlanamadı." };

    public override IdentityError ConcurrencyFailure() =>
        new() { Code = nameof(ConcurrencyFailure), Description = "Kayıt başka bir işlemle değişti; lütfen tekrar deneyin." };
}
