using System.Security.Claims;
using GameVault.DataAccess;
using GameVault.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace GameVault.Web.Infrastructure;

/// <summary>
/// ASP.NET Core cookie kimlik doğrulamasının resmi <see cref="ITicketStore"/> genişleme
/// noktası üzerinden sunucu tarafı oturum saklama. Çerez yalnızca oturum anahtarını
/// taşır; çıkışta, parola sıfırlamada ve hesap silmede kayıt silinir ve eski çerez
/// anında geçersiz olur.
/// </summary>
public sealed class DbTicketStore(IServiceScopeFactory scopes, TimeProvider time) : ITicketStore
{
    public const string SessionIdItem = "gv.sid";
    private static readonly TimeSpan FallbackLifetime = TimeSpan.FromDays(7);

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var id = Guid.NewGuid();
        ticket.Properties.Items[SessionIdItem] = id.ToString("N");

        var userIdValue = ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
        {
            throw new InvalidOperationException("Oturum bileti kullanıcı kimliği içermiyor.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>();
        db.UserSessions.Add(new UserSession
        {
            Id = id,
            UserId = userId,
            Ticket = TicketSerializer.Default.Serialize(ticket),
            CreatedAtUtc = now,
            ExpiresAtUtc = ticket.Properties.ExpiresUtc?.UtcDateTime ?? now.Add(FallbackLifetime)
        });
        await db.SaveChangesAsync();
        return id.ToString("N");
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        if (!Guid.TryParseExact(key, "N", out var id))
        {
            return;
        }

        ticket.Properties.Items[SessionIdItem] = key;
        var now = time.GetUtcNow().UtcDateTime;
        var ticketBytes = TicketSerializer.Default.Serialize(ticket);
        var expires = ticket.Properties.ExpiresUtc?.UtcDateTime ?? now.Add(FallbackLifetime);

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>();
        // Silinmiş (iptal edilmiş) bir oturum yenilemeyle geri gelmez: yalnızca var olan kayıt güncellenir.
        await db.UserSessions
            .Where(s => s.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Ticket, ticketBytes)
                .SetProperty(x => x.ExpiresAtUtc, expires));
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        if (!Guid.TryParseExact(key, "N", out var id))
        {
            return null;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>();
        var session = await db.UserSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id);
        if (session is null)
        {
            return null;
        }

        if (session.ExpiresAtUtc <= time.GetUtcNow().UtcDateTime)
        {
            await db.UserSessions.Where(s => s.Id == id).ExecuteDeleteAsync();
            return null;
        }

        return TicketSerializer.Default.Deserialize(session.Ticket);
    }

    public async Task RemoveAsync(string key)
    {
        if (!Guid.TryParseExact(key, "N", out var id))
        {
            return;
        }

        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GameVaultDbContext>();
        await db.UserSessions.Where(s => s.Id == id).ExecuteDeleteAsync();
    }

    /// <summary>Geçerli isteğin sunucu oturum kimliği (yoksa null).</summary>
    public static Guid? CurrentSessionId(AuthenticateResult? auth) =>
        auth?.Properties?.Items.TryGetValue(SessionIdItem, out var value) == true
        && Guid.TryParseExact(value, "N", out var id)
            ? id
            : null;
}
