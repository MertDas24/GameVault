using System.Net;
using System.Net.Sockets;

namespace GameVault.Web.Infrastructure;

/// <summary>
/// Oran sınırlama için istemci bölüm anahtarı. RemoteIpAddress, yalnızca yapılandırılmış
/// güvenilir proxy'lerden gelen X-Forwarded-For başlığıyla değiştirilir (bkz. ForwardedHeaders).
/// IPv6 adresleri /64 önekine indirgenir; tek bir istemcinin adres döndürerek sınırı
/// aşması zorlaşır.
/// </summary>
public static class ClientPartition
{
    public static string ForIp(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip is null)
        {
            return "ip:unknown";
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = ip.GetAddressBytes();
            Array.Clear(bytes, 8, 8);
            ip = new IPAddress(bytes);
        }

        return "ip:" + ip;
    }

    public static string ForEmail(string? email) =>
        "email:" + (email ?? string.Empty).Trim().ToUpperInvariant();

    public static string ForUser(Guid userId) => "user:" + userId.ToString("N");
}
