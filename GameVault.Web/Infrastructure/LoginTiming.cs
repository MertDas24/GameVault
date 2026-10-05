using System.Security.Cryptography;
using GameVault.Entities;
using Microsoft.AspNetCore.Identity;

namespace GameVault.Web.Infrastructure;

/// <summary>
/// Kayıtlı olmayan bir e-postayla giriş denendiğinde de parola doğrulaması kadar iş
/// yapar; böylece yanıt süresi hesabın var olup olmadığını belli etmez.
/// </summary>
public sealed class LoginTiming(IPasswordHasher<ApplicationUser> hasher)
{
    private static readonly ApplicationUser DummyUser = new();
    private static readonly Lock HashLock = new();
    private static string? _dummyHash;

    public void Equalize(string password)
    {
        string hash;
        lock (HashLock)
        {
            hash = _dummyHash ??= hasher.HashPassword(DummyUser, Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)));
        }

        hasher.VerifyHashedPassword(DummyUser, hash, password);
    }
}
