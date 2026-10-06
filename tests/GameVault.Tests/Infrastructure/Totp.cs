using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;

namespace GameVault.Tests.Infrastructure;

/// <summary>
/// Testlerde doğrulayıcı uygulama yerine geçen bağımsız RFC 6238 (TOTP) hesaplayıcı: HMAC-SHA1, 30 sn, 6 hane.
/// Identity'nin iç sınıflarını kullanmaz; böylece testler "standart bir uygulama bu anahtarla geçerli kod üretiyor mu?"
/// sorusunu da yanıtlar.
/// </summary>
public static class Totp
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Security", "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "RFC 6238 ve doğrulayıcı uygulamalar HMAC-SHA1 kullanır; burada HMAC olarak kullanılır, özet olarak değil.")]
    public static string Compute(string base32Key, DateTimeOffset at)
    {
        var key = FromBase32(base32Key);
        Span<byte> counter = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counter, at.ToUnixTimeSeconds() / 30);

        var hash = HMACSHA1.HashData(key, counter);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6", CultureInfo.InvariantCulture);
    }

    /// <summary>Şu anki koddan farklı ama hâlâ kabul penceresinde olan bir kod (önceki 30 sn'lik adım).</summary>
    public static string Previous(string base32Key) => Compute(base32Key, DateTimeOffset.UtcNow.AddSeconds(-30));

    public static string Now(string base32Key) => Compute(base32Key, DateTimeOffset.UtcNow);

    private static byte[] FromBase32(string input)
    {
        var clean = input.Replace(" ", string.Empty, StringComparison.Ordinal).TrimEnd('=').ToUpperInvariant();
        var output = new List<byte>(clean.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach (var c in clean)
        {
            var value = Base32Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (value < 0)
            {
                throw new FormatException($"Geçersiz base32 karakteri: {c}");
            }

            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)(buffer >> bits));
                buffer &= (1 << bits) - 1;
            }
        }

        return [.. output];
    }
}
