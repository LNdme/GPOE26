using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace GPOE26.Agent.Abstractions;

public static class McpChatServiceProtocol
{
    public const string TimestampHeader = "X-GPOE26-Mcp-Chat-Time";
    public const string SignatureHeader = "X-GPOE26-Mcp-Chat-Signature";
    public const int AllowedClockSkewSeconds = 30;

    public static string CreateSignature(
        string sharedKey,
        string method,
        string pathAndQuery,
        Guid userId,
        long unixTimeSeconds)
    {
        if (!IsValidSharedKey(sharedKey))
            throw new ArgumentException("The MCP Chat service key must contain at least 32 UTF-8 bytes.", nameof(sharedKey));

        var payload = Encoding.UTF8.GetBytes(
            $"{method.ToUpperInvariant()}\n{pathAndQuery}\n{userId:N}\n{unixTimeSeconds.ToString(CultureInfo.InvariantCulture)}");
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(sharedKey), payload);
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool IsValidSignature(
        string? sharedKey,
        string method,
        string pathAndQuery,
        Guid userId,
        string? timestamp,
        string? signature,
        DateTimeOffset now)
    {
        if (!IsValidSharedKey(sharedKey) ||
            !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var unixTimeSeconds) ||
            string.IsNullOrEmpty(signature))
            return false;

        var nowSeconds = now.ToUnixTimeSeconds();
        if (unixTimeSeconds < nowSeconds - AllowedClockSkewSeconds ||
            unixTimeSeconds > nowSeconds + AllowedClockSkewSeconds)
            return false;

        var expected = Encoding.ASCII.GetBytes(
            CreateSignature(sharedKey, method, pathAndQuery, userId, unixTimeSeconds));
        var provided = Encoding.ASCII.GetBytes(signature);
        return expected.Length == provided.Length &&
               CryptographicOperations.FixedTimeEquals(expected, provided);
    }

    public static bool IsValidSharedKey([NotNullWhen(true)] string? sharedKey) =>
        !string.IsNullOrEmpty(sharedKey) &&
        Encoding.UTF8.GetByteCount(sharedKey) >= 32;
}
