namespace GameVault.Models.Library;

/// <summary>Koleksiyon adları ve sınırları; form, servis ve içe aktarma aynı kuralları kullanır.</summary>
public static class CollectionRules
{
    public const int NameMaxLength = 40;
    public const int MaxPerUser = 50;
    public const int MaxPerGame = 10;

    /// <summary>"Yeni koleksiyonlar" alanındaki virgülle ayrılmış adları ayırır; boşları ve tekrarları atar.</summary>
    public static IReadOnlyList<string> SplitNames(string? raw) =>
        string.IsNullOrWhiteSpace(raw)
            ? []
            : raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Select(Normalize)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

    /// <summary>Baştaki/sondaki boşlukları atar, aradaki ardışık boşlukları teke indirir.</summary>
    public static string Normalize(string name) =>
        string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Ad geçerliyse null, değilse kullanıcıya gösterilecek hata.</summary>
    public static string? NameError(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Koleksiyon adı boş olamaz.";
        }

        if (name.Length > NameMaxLength)
        {
            return $"Koleksiyon adı en fazla {NameMaxLength} karakter olabilir.";
        }

        // Virgül, "yeni koleksiyonlar" alanında ayırıcıdır; adın içinde olamaz.
        if (name.Contains(',') || name.Any(char.IsControl))
        {
            return "Koleksiyon adında virgül ya da kontrol karakteri olamaz.";
        }

        return null;
    }
}
