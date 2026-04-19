namespace MapIslandEditor;

public sealed class MapObjectMovedEventArgs : EventArgs
{
    public int Index { get; init; }
    public int OldGridPosX { get; init; }
    public int OldGridPosY { get; init; }
    public int GridPosX { get; init; }
    public int GridPosY { get; init; }
}
