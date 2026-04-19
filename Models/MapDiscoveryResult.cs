namespace MapIslandEditor.Models;

public sealed class MapDiscoveryResult
{
    public required string MapGridPath { get; init; }
    public required string MapObjectPath { get; init; }
    public required string FirstPackPath { get; init; }
    public required IReadOnlyList<string> CandidateModelPaths { get; init; }
}
