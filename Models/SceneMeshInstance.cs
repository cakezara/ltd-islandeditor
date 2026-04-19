namespace MapIslandEditor.Models;

public sealed class SceneMeshInstance
{
    public required BfresMeshData Mesh { get; init; }
    public required string Name { get; init; }
    public required uint Hash { get; init; }
    public required float GridX { get; init; }
    public required float GridY { get; init; }
    public required float RotY { get; init; }
}
