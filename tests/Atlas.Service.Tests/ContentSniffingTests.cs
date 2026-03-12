using Atlas.Core.Contracts;
using Atlas.Core.Policies;
using Atlas.Core.Scanning;
using Atlas.Service.Services;

namespace Atlas.Service.Tests;

public sealed class ContentSniffingTests : IDisposable
{
    private readonly string _testRoot;
    private readonly FileScanner _scanner;
    private readonly PolicyProfile _profile;

    public ContentSniffingTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"atlas-sniff-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);

        _scanner = new FileScanner(new PathSafetyClassifier());
        _profile = new PolicyProfile
        {
            ScanRoots = [_testRoot],
            MutableRoots = [_testRoot]
        };
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, recursive: true); }
        catch { }
    }

    private string WriteFile(string name, byte[] content)
    {
        var path = Path.Combine(_testRoot, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    // ── Signature detection tests ────────────────────────────────────

    [Fact]
    public void Sniff_DetectsPdf()
    {
        var path = WriteFile("test.pdf", "%PDF-1.4 fake body"u8.ToArray());
        var sig = ContentSniffer.Sniff(path);

        Assert.NotNull(sig);
        Assert.Equal("application/pdf", sig.MimeType);
        Assert.Equal("Documents", sig.Category);
    }

    [Fact]
    public void Sniff_DetectsPng()
    {
        var path = WriteFile("test.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00]);
        var sig = ContentSniffer.Sniff(path);

        Assert.NotNull(sig);
        Assert.Equal("image/png", sig.MimeType);
        Assert.Equal("Images", sig.Category);
    }

    [Fact]
    public void Sniff_DetectsJpeg()
    {
        var path = WriteFile("test.jpg", [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]);
        var sig = ContentSniffer.Sniff(path);

        Assert.NotNull(sig);
        Assert.Equal("image/jpeg", sig.MimeType);
        Assert.Equal("Images", sig.Category);
    }

    [Fact]
    public void Sniff_DetectsGif()
    {
        var path = WriteFile("test.gif", "GIF89a fake"u8.ToArray());
        var sig = ContentSniffer.Sniff(path);

        Assert.NotNull(sig);
        Assert.Equal("image/gif", sig.MimeType);
        Assert.Equal("Images", sig.Category);
    }

    [Fact]
    public void Sniff_DetectsWebP()
    {
        // RIFF + 4-byte size + WEBP
        byte[] header = [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50, 0x00];
        var path = WriteFile("test.webp", header);
        var sig = ContentSniffer.Sniff(path);

        Assert.NotNull(sig);
        Assert.Equal("image/webp", sig.MimeType);
        Assert.Equal("Images", sig.Category);
    }

    [Fact]
    public void Sniff_DetectsZip()
    {
        // Minimal ZIP local file header with a generic filename
        byte[] header =
        [
            0x50, 0x4B, 0x03, 0x04, // PK\x03\x04
            0x14, 0x00, 0x00, 0x00, 0x08, 0x00, // version, flags, compression
            0x00, 0x00, 0x00, 0x00, // mod time/date
            0x00, 0x00, 0x00, 0x00, // crc-32
            0x00, 0x00, 0x00, 0x00, // compressed size
            0x00, 0x00, 0x00, 0x00, // uncompressed size
            0x08, 0x00,             // filename length = 8
            0x00, 0x00,             // extra field length = 0
            // filename: "data.txt"
            0x64, 0x61, 0x74, 0x61, 0x2E, 0x74, 0x78, 0x74
        ];
        var path = WriteFile("test.zip", header);
        var sig = ContentSniffer.Sniff(path);

        Assert.NotNull(sig);
        Assert.Equal("application/zip", sig.MimeType);
        Assert.Equal("Archives", sig.Category);
    }

    [Fact]
    public void Sniff_DetectsMp3WithId3()
    {
        byte[] header = [0x49, 0x44, 0x33, 0x03, 0x00, 0x00]; // ID3v2.3
        var path = WriteFile("test.mp3", header);
        var sig = ContentSniffer.Sniff(path);

        Assert.NotNull(sig);
        Assert.Equal("audio/mpeg", sig.MimeType);
        Assert.Equal("Audio", sig.Category);
    }

    [Fact]
    public void Sniff_DetectsWav()
    {
        // RIFF + 4-byte size + WAVE
        byte[] header = [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45, 0x66];
        var path = WriteFile("test.wav", header);
        var sig = ContentSniffer.Sniff(path);

        Assert.NotNull(sig);
        Assert.Equal("audio/wav", sig.MimeType);
        Assert.Equal("Audio", sig.Category);
    }

    [Fact]
    public void Sniff_DetectsMp4()
    {
        // MP4 ftyp box: 4-byte size + "ftyp" + brand
        byte[] header = [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D];
        var path = WriteFile("test.mp4", header);
        var sig = ContentSniffer.Sniff(path);

        Assert.NotNull(sig);
        Assert.Equal("video/mp4", sig.MimeType);
        Assert.Equal("Video", sig.Category);
    }

    // ── Fallback and mismatch tests ──────────────────────────────────

    [Fact]
    public void Sniff_ReturnsNull_ForUnknownContent()
    {
        var path = WriteFile("unknown.dat", "just some random text content"u8.ToArray());
        var sig = ContentSniffer.Sniff(path);

        Assert.Null(sig);
    }

    [Fact]
    public void Scanner_FallsBackToExtension_WhenSniffFails()
    {
        var path = WriteFile("notes.txt", "plain text that matches no magic"u8.ToArray());
        var item = _scanner.InspectFile(_profile, path);

        Assert.NotNull(item);
        Assert.Equal("Documents", item.Category);
        Assert.Equal("txt", item.MimeType);
        Assert.Equal(string.Empty, item.ContentFingerprint);
    }

    [Fact]
    public void Scanner_ContentWins_WhenExtensionMismatches()
    {
        // PNG header in a .txt file
        var path = WriteFile("secret.txt", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00]);
        var item = _scanner.InspectFile(_profile, path);

        Assert.NotNull(item);
        Assert.Equal("image/png", item.MimeType);
        Assert.Equal("Images", item.Category);
    }

    [Fact]
    public void Sniff_ReturnsNull_ForEmptyFile()
    {
        var path = WriteFile("empty.pdf", []);
        var sig = ContentSniffer.Sniff(path);

        Assert.Null(sig);
    }

    // ── Fingerprint tests ────────────────────────────────────────────

    [Fact]
    public void Sniff_PopulatesFingerprint_ForKnownFile()
    {
        var path = WriteFile("fp.pdf", "%PDF-1.4 test content for fingerprint"u8.ToArray());
        var sig = ContentSniffer.Sniff(path);

        Assert.NotNull(sig);
        Assert.NotEmpty(sig.HeaderFingerprint);
        Assert.Equal(64, sig.HeaderFingerprint.Length); // SHA-256 hex = 64 chars
    }

    [Fact]
    public void Sniff_Fingerprint_BoundedToFirstEightKb()
    {
        // Create a file larger than 8KB with PDF header
        var content = new byte[16 * 1024];
        "%PDF-1.4"u8.CopyTo(content);
        var path = WriteFile("large.pdf", content);

        var sig = ContentSniffer.Sniff(path);
        Assert.NotNull(sig);

        // Now create the same file but with different content after 8KB
        var content2 = new byte[16 * 1024];
        "%PDF-1.4"u8.CopyTo(content2);
        content2[12000] = 0xFF; // change after 8KB boundary
        var path2 = WriteFile("large2.pdf", content2);

        var sig2 = ContentSniffer.Sniff(path2);
        Assert.NotNull(sig2);

        // Fingerprints should be identical because only first 8KB is hashed
        Assert.Equal(sig.HeaderFingerprint, sig2.HeaderFingerprint);
    }

    // ── Integration: InspectFile and ScanAsync both use sniffing ─────

    [Fact]
    public void InspectFile_UsesSniffer()
    {
        var path = WriteFile("doc.pdf", "%PDF-1.7 some document"u8.ToArray());
        var item = _scanner.InspectFile(_profile, path);

        Assert.NotNull(item);
        Assert.Equal("application/pdf", item.MimeType);
        Assert.Equal("Documents", item.Category);
        Assert.NotEmpty(item.ContentFingerprint);
    }

    [Fact]
    public async Task ScanAsync_UsesSniffer()
    {
        WriteFile("scan-test.png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00]);

        var response = await _scanner.ScanAsync(_profile, new ScanRequest
        {
            Roots = { _testRoot }
        }, CancellationToken.None);

        var pngItem = response.Inventory.FirstOrDefault(f => f.Name == "scan-test.png");
        Assert.NotNull(pngItem);
        Assert.Equal("image/png", pngItem.MimeType);
        Assert.Equal("Images", pngItem.Category);
        Assert.NotEmpty(pngItem.ContentFingerprint);
    }
}
