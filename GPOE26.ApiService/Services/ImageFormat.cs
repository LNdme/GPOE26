namespace GPOE26.ApiService.Services;

/// <summary>
/// Reconnaît une image à ses premiers octets plutôt qu'à son extension :
/// une photo « .jfif » ou « .JPEG » est un JPEG, et un fichier renommé en « .png » n'en devient pas une image.
/// </summary>
public static class ImageFormat
{
    /// <summary>Octets à lire pour reconnaître le format.</summary>
    public const int HeaderLength = 12;

    /// <summary>Extension à donner au fichier enregistré, ou null si ce n'est pas une image affichable sur le site.</summary>
    public static string? ExtensionOf(ReadOnlySpan<byte> header) => header switch
    {
        [0xFF, 0xD8, 0xFF, ..] => ".jpg",
        [0x89, (byte)'P', (byte)'N', (byte)'G', ..] => ".png",
        [(byte)'G', (byte)'I', (byte)'F', (byte)'8', ..] => ".gif",
        [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => ".webp",
        [_, _, _, _, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'a', (byte)'v', (byte)'i', (byte)'f' or (byte)'s', ..] => ".avif",
        _ => null
    };

    /// <summary>Photo HEIC/HEIF (iPhone) : la plupart des navigateurs ne savent pas l'afficher.</summary>
    public static bool IsHeic(ReadOnlySpan<byte> header) =>
        header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8) &&
        (header[8..12].SequenceEqual("heic"u8) || header[8..12].SequenceEqual("heix"u8) ||
         header[8..12].SequenceEqual("mif1"u8) || header[8..12].SequenceEqual("msf1"u8));

    /// <summary>Message à afficher quand le fichier est refusé.</summary>
    public static string RejectionMessage(ReadOnlySpan<byte> header) => IsHeic(header)
        ? "Photo au format HEIC (iPhone) : les navigateurs ne savent pas l'afficher. Enregistrez-la en JPEG puis réessayez."
        : "Ce fichier n'est pas une image acceptée. Formats acceptés : JPEG, PNG, GIF, WebP ou AVIF.";
}
