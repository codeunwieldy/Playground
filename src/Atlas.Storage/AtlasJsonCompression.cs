using System.IO.Compression;
using System.Text;

namespace Atlas.Storage;

public static class AtlasJsonCompression
{
    public static byte[] Compress(string payload)
    {
        var raw = Encoding.UTF8.GetBytes(payload);
        using var output = new MemoryStream();
        using (var brotli = new BrotliStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            brotli.Write(raw, 0, raw.Length);
        }

        return output.ToArray();
    }

    public static string Decompress(byte[] payload)
    {
        using var input = new MemoryStream(payload);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        brotli.CopyTo(output);
        return Encoding.UTF8.GetString(output.ToArray());
    }
}