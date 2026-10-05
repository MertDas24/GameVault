namespace GameVault.Entities;

/// <summary>Tüm kullanıcıların ortak kullandığı, salt okunur başvuru verisi.</summary>
public class Genre
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<LibraryGame> Games { get; set; } = new List<LibraryGame>();
}
