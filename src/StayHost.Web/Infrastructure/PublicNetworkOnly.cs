using System.Net;
using System.Net.Sockets;

namespace StayHost.Web.Infrastructure;

/// <summary>
/// docs/01 QL-10 — a calendar address is typed by a host, and any signed-in user
/// can become one. Fetching it as given let that address be the database, the
/// translation container or the cloud metadata endpoint, with the feed's error
/// line reporting back what answered.
///
/// The check sits in the socket's connect step rather than in front of the
/// request, so it also covers a redirect and a name that resolves differently
/// the second time it is asked.
/// </summary>
public sealed class NotPublicAddressException()
    : HttpRequestException("Địa chỉ lịch không trỏ ra internet công cộng.");

public static class PublicNetworkOnly
{
    public static SocketsHttpHandler Handler() => new()
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 3,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        ConnectCallback = ConnectAsync
    };

    private static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);

        // Every answer must be public: a name with one public and one private
        // record would otherwise be a coin toss away from the inside.
        if (addresses.Length == 0 || addresses.Any(a => !IsPublic(a)))
            throw new NotPublicAddressException();

        var target = addresses[0];
        var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(target, context.DnsEndPoint.Port), ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address)) return false;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0
                     || b[0] == 10
                     || b[0] == 127
                     || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                     || (b[0] == 169 && b[1] == 254)
                     || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                     || (b[0] == 192 && b[1] == 168)
                     || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                     || (b[0] == 198 && (b[1] == 18 || b[1] == 19))
                     || b[0] >= 224);
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.Equals(IPAddress.IPv6None) || address.Equals(IPAddress.IPv6Any)) return false;
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return false;

            var first = address.GetAddressBytes()[0];
            return (first & 0xFE) != 0xFC; // fc00::/7, unique local
        }

        return false;
    }
}
