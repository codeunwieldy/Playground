using System.Security.Cryptography;

namespace Atlas.Core.Scanning;

/// <summary>
/// Result of content-based file sniffing.
/// </summary>
public sealed record ContentSignature(
    string MimeType,
    string Category,
    string HeaderFingerprint);

/// <summary>
/// Bounded header-based content sniffer for common file families.
/// Reads at most a small bounded header to detect file type via magic bytes,
/// then computes a SHA-256 fingerprint of the first 8KB.
/// Returns null when the file cannot be opened, is empty, or doesn't match any known signature.
/// </summary>
public static class ContentSniffer
{
    private const int HeaderReadSize = 68; // enough for ZIP local file header filename
    private const int FingerprintSize = 8 * 1024;

    public static ContentSignature? Sniff(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length == 0) return null;

            var headerLen = (int)Math.Min(HeaderReadSize, stream.Length);
            var header = new byte[headerLen];
            _ = stream.Read(header, 0, headerLen);

            var detected = DetectSignature(header, headerLen);
            if (detected is null) return null;

            // Compute bounded header fingerprint (first 8KB).
            stream.Position = 0;
            var fpLen = (int)Math.Min(FingerprintSize, stream.Length);
            var fpBuffer = new byte[fpLen];
            _ = stream.Read(fpBuffer, 0, fpLen);
            var fingerprint = Convert.ToHexString(SHA256.HashData(fpBuffer)).ToLowerInvariant();

            return new ContentSignature(detected.Value.Mime, detected.Value.Category, fingerprint);
        }
        catch
        {
            return null;
        }
    }

    private static (string Mime, string Category)? DetectSignature(byte[] header, int length)
    {
        if (length < 3) return null;

        // PDF: %PDF
        if (length >= 4 && header[0] == 0x25 && header[1] == 0x50 && header[2] == 0x44 && header[3] == 0x46)
            return ("application/pdf", "Documents");

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (length >= 8
            && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
            && header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
            return ("image/png", "Images");

        // JPEG: FF D8 FF
        if (header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return ("image/jpeg", "Images");

        // GIF: GIF87a or GIF89a
        if (length >= 6
            && header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38
            && (header[4] == 0x37 || header[4] == 0x39) && header[5] == 0x61)
            return ("image/gif", "Images");

        // RIFF container: WebP or WAV
        if (length >= 12
            && header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46)
        {
            // WebP: bytes 8-11 = WEBP
            if (header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
                return ("image/webp", "Images");

            // WAV: bytes 8-11 = WAVE
            if (header[8] == 0x57 && header[9] == 0x41 && header[10] == 0x56 && header[11] == 0x45)
                return ("audio/wav", "Audio");
        }

        // ZIP: PK\x03\x04 — then sub-sniff for Office
        if (length >= 4
            && header[0] == 0x50 && header[1] == 0x4B && header[2] == 0x03 && header[3] == 0x04)
            return DetectZipContents(header, length);

        // MP3 with ID3 tag
        if (header[0] == 0x49 && header[1] == 0x44 && header[2] == 0x33)
            return ("audio/mpeg", "Audio");

        // MP3 sync word: FF FB, FF FA, FF F3, FF F2
        if (header[0] == 0xFF && (header[1] == 0xFB || header[1] == 0xFA || header[1] == 0xF3 || header[1] == 0xF2))
            return ("audio/mpeg", "Audio");

        // MP4/MOV: bytes 4-7 = "ftyp"
        if (length >= 8
            && header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70)
            return ("video/mp4", "Video");

        return null;
    }

    /// <summary>
    /// After detecting ZIP magic, look at the local file header's filename field
    /// to detect Office Open XML containers.
    /// ZIP local file header layout:
    ///   offset 26: filename length (2 bytes LE)
    ///   offset 30: filename (variable)
    /// </summary>
    private static (string Mime, string Category) DetectZipContents(byte[] header, int length)
    {
        if (length >= 34)
        {
            var filenameLen = header[26] | (header[27] << 8);
            if (filenameLen > 0 && 30 + filenameLen <= length)
            {
                var filename = System.Text.Encoding.ASCII.GetString(header, 30, filenameLen);

                if (filename.StartsWith("word/", StringComparison.OrdinalIgnoreCase))
                    return ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "Documents");

                if (filename.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
                    return ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Spreadsheets");

                if (filename.StartsWith("ppt/", StringComparison.OrdinalIgnoreCase))
                    return ("application/vnd.openxmlformats-officedocument.presentationml.presentation", "Presentations");

                if (filename.Equals("[Content_Types].xml", StringComparison.OrdinalIgnoreCase))
                    return ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", "Documents");
            }
        }

        return ("application/zip", "Archives");
    }
}
