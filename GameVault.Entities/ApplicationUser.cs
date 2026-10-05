using Microsoft.AspNetCore.Identity;

namespace GameVault.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<LibraryGame> Games { get; set; } = new List<LibraryGame>();
}
