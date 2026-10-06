namespace GameVault.Entities;

/// <summary>
/// Kullanıcının kendi oluşturduğu oyun koleksiyonu (etiket). Türlerin aksine ortak değildir: her koleksiyon
/// tek bir kullanıcıya aittir ve yalnızca o kullanıcının oyunlarıyla ilişkilendirilebilir.
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix",
    Justification = "Alan adı: kullanıcının oyun koleksiyonu. .NET koleksiyon türü değildir.")]
public class GameCollection
{
    public int Id { get; set; }

    public Guid UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Oluşturulma zamanı (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; }

    public ICollection<LibraryGame> Games { get; set; } = new List<LibraryGame>();
}
