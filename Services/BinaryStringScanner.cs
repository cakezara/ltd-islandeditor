using System.Text;

namespace MapIslandEditor.Services;

public static class BinaryStringScanner
{
    public static IEnumerable<string> ExtractAsciiStrings(byte[] data, int minimumLength = 4)
    {
        var current = new List<byte>();
        for (var i = 0; i < data.Length; i++)
        {
            var b = data[i];
            if (b >= 32 && b <= 126)
            {
                current.Add(b);
                continue;
            }

            if (current.Count >= minimumLength)
            {
                yield return Encoding.ASCII.GetString(current.ToArray());
            }

            current.Clear();
        }

        if (current.Count >= minimumLength)
        {
            yield return Encoding.ASCII.GetString(current.ToArray());
        }
    }
}
