namespace MapIslandEditor.Models;

public sealed class MapObjectEntry
{
    public string Id { get; set; } = string.Empty;
    public uint Hash { get; set; }
    public int GridPosX { get; set; }
    public int GridPosY { get; set; }
    public float RotY { get; set; }
}
