using ZstdSharp;

namespace MapIslandEditor.Services;

public sealed class ZsDecompressionService
{
    public byte[] ReadPossiblyCompressed(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        if (!filePath.EndsWith(".zs", StringComparison.OrdinalIgnoreCase))
        {
            return bytes;
        }

        using var decompressor = new Decompressor();
        return decompressor.Unwrap(bytes).ToArray();
    }
}
