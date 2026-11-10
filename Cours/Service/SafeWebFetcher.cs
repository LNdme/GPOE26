using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Cours.Service;

/// <summary>Échec d'import expliqué à l'utilisateur.</summary>
public class CourseImportException(string message) : Exception(message);

public record FetchedResource(byte[] Data, string MediaType, Uri FinalUri, string? FileName);

/// <summary>
/// Télécharge une adresse donnée par un utilisateur. Le service tourne à côté de la base et des autres API :
/// seules les adresses publiques sont joignables, y compris après une redirection ou un changement DNS.
/// </summary>
public static partial class SafeWebFetcher
{
    private static readonly HttpClient Http = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 5,
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        AutomaticDecompression = DecompressionMethods.All,
        ConnectCallback = ConnectToPublicHostAsync
    })
    {
        Timeout = TimeSpan.FromSeconds(40)
    };

    /// <summary>Contrôle fait à chaque connexion : c'est lui qui bloque les redirections vers le réseau interne.</summary>
    private static async ValueTask<Stream> ConnectToPublicHostAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        if (context.DnsEndPoint.Port is not (80 or 443))
            throw new HttpRequestException("Port non autorisé.");

        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
        if (addresses.Length == 0 || !addresses.All(IsPublicAddress))
            throw new HttpRequestException("Adresse non autorisée.");

        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address)) return false;

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            var isUnspecified = address.Equals(IPAddress.IPv6None);
            var isUniqueLocal = (bytes[0] & 0xFE) == 0xFC;
            var isDocumentation = bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8;
            var isNat64 = bytes[0] == 0x00 && bytes[1] == 0x64 && bytes[2] == 0xFF && bytes[3] == 0x9B;
            return !(isUnspecified || address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || isUniqueLocal || isDocumentation || isNat64);
        }

        var b = address.GetAddressBytes();
        return !(b[0] == 0 || b[0] == 10 || b[0] == 127 || b[0] >= 224
                 || (b[0] == 100 && b[1] is >= 64 and <= 127)
                 || (b[0] == 169 && b[1] == 254)
                 || (b[0] == 172 && b[1] is >= 16 and <= 31)
                 || (b[0] == 192 && b[1] == 0 && b[2] == 0)
                 || (b[0] == 192 && b[1] == 168)
                 || (b[0] == 198 && b[1] is 18 or 19));
    }

    /// <summary>Lien recevable : http(s), hôte public, sans identifiants intégrés.</summary>
    public static Uri ValidateUrl(string? input)
    {
        var text = input?.Trim() ?? "";
        if (text.Length == 0)
            throw new CourseImportException("Collez d'abord le lien du cours.");
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || string.IsNullOrEmpty(uri.Host))
            throw new CourseImportException("Ce lien n'est pas valide : il doit commencer par http:// ou https://.");
        if (!string.IsNullOrEmpty(uri.UserInfo) || !uri.IsDefaultPort && uri.Port is not (80 or 443))
            throw new CourseImportException("Ce lien n'est pas pris en charge.");
        if (uri.IsLoopback || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            || (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var ip) && !IsPublicAddress(ip)))
            throw new CourseImportException("Ce lien pointe vers une adresse privée : seules les pages publiques peuvent être importées.");
        return uri;
    }

    /// <summary>Un lien de partage ouvre une page de visualisation : on vise directement le document.</summary>
    public static Uri NormalizeShareLink(Uri uri)
    {
        if (uri.Host.Equals("drive.google.com", StringComparison.OrdinalIgnoreCase))
        {
            var match = DriveFile().Match(uri.AbsolutePath);
            var id = match.Success ? match.Groups[1].Value : QueryValue(uri, "id");
            if (!string.IsNullOrEmpty(id))
                return new Uri($"https://drive.google.com/uc?export=download&id={Uri.EscapeDataString(id)}");
        }
        if (uri.Host.Equals("docs.google.com", StringComparison.OrdinalIgnoreCase))
        {
            var match = GoogleDocument().Match(uri.AbsolutePath);
            if (match.Success)
            {
                var id = Uri.EscapeDataString(match.Groups[2].Value);
                return new Uri(match.Groups[1].Value == "presentation"
                    ? $"https://docs.google.com/presentation/d/{id}/export/pdf"
                    : $"https://docs.google.com/document/d/{id}/export?format=pdf");
            }
        }
        if (uri.Host.EndsWith("dropbox.com", StringComparison.OrdinalIgnoreCase) && uri.Query.Contains("dl=0", StringComparison.Ordinal))
            return new Uri(uri.AbsoluteUri.Replace("dl=0", "dl=1", StringComparison.Ordinal));
        return uri;
    }

    public static bool IsGoogleHost(Uri uri) =>
        uri.Host.EndsWith("google.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith("googleusercontent.com", StringComparison.OrdinalIgnoreCase);

    public static async Task<FetchedResource> FetchAsync(Uri uri, long maxBytes, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("GPOE26-CourseImport/1.0");
        request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/pdf,image/*;q=0.8,*/*;q=0.5");

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new CourseImportException("Ce lien n'a pas pu être ouvert. Vérifiez qu'il est correct et que la page est publique.");
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                throw new CourseImportException("Cette page est privée : elle demande une connexion. Partagez-la publiquement ou importez le PDF.");
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                throw new CourseImportException("Cette page n'existe plus à cette adresse.");
            if (!response.IsSuccessStatusCode)
                throw new CourseImportException($"Le site a refusé la demande (code {(int)response.StatusCode}).");
            if (response.Content.Headers.ContentLength > maxBytes)
                throw new CourseImportException($"Ce document dépasse la taille maximale de {maxBytes / (1024 * 1024)} Mo.");

            var data = await ReadLimitedAsync(response.Content, maxBytes, ct);
            return new FetchedResource(data, response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? "",
                response.RequestMessage?.RequestUri ?? uri, FileNameOf(response.Content.Headers.ContentDisposition));
        }
    }

    private static string? FileNameOf(ContentDispositionHeaderValue? header) =>
        (header?.FileNameStar ?? header?.FileName)?.Trim('"', ' ');

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, long maxBytes, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        using var memory = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            memory.Write(buffer, 0, read);
            if (memory.Length > maxBytes)
                throw new CourseImportException($"Ce document dépasse la taille maximale de {maxBytes / (1024 * 1024)} Mo.");
        }
        return memory.ToArray();
    }

    private static string? QueryValue(Uri uri, string key) =>
        uri.Query.TrimStart('?').Split('&').Select(p => p.Split('=', 2)).FirstOrDefault(p => p.Length == 2 && p[0] == key)?[1];

    [GeneratedRegex(@"^/file/d/([\w-]+)")] private static partial Regex DriveFile();
    [GeneratedRegex(@"^/(document|presentation)/d/([\w-]+)")] private static partial Regex GoogleDocument();
}
