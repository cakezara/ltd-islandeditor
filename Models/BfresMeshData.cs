namespace MapIslandEditor.Models;

public sealed class BfresMeshData
{
    public required string SourcePath { get; init; }
    public required string DisplayName { get; init; }
    public required List<float> Positions { get; init; }
    public required List<int> Indices { get; init; }
    public int VertexCount => Positions.Count / 3;
    public int TriangleCount => Indices.Count / 3;
}
