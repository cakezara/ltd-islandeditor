using Revrs;

namespace MapIslandEditor.Models;

public sealed class MapProject
{
    public string Name { get; set; } = string.Empty;
    public string MapKey { get; set; } = string.Empty;
    public string GridFilePath { get; set; } = string.Empty;
    public string ObjectFilePath { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public uint[,] Grid { get; set; } = new uint[0, 0];
    public uint[,] UgcFloor { get; set; } = new uint[0, 0];
    public uint[,]? InvalidGridFlag { get; set; }
    public string GridSizeType { get; set; } = string.Empty;
    public bool CanBeFocus { get; set; }
    public bool CanEnterSequence { get; set; }
    public bool HasInvalidGridFlag { get; set; }
    public Endianness GridEndianness { get; set; }
    public ushort GridVersion { get; set; }
    public Endianness ObjectEndianness { get; set; }
    public ushort ObjectVersion { get; set; }
    public List<MapObjectEntry> Objects { get; set; } = [];

    public override string ToString()
    {
        return $"{Name} ({Width}x{Height}, {Objects.Count} objects)";
    }
}
