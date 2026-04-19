using System.Text.RegularExpressions;
using MapIslandEditor.Models;

namespace MapIslandEditor.Services;

public sealed class MapAssetLocator
{
    private static readonly Regex TokenRegex = new("^[A-Za-z][A-Za-z0-9_]{2,}$", RegexOptions.Compiled);
    private readonly string _rootPath;

    public MapAssetLocator(string rootPath)
    {
        _rootPath = rootPath;
    }

    public MapDiscoveryResult DiscoverFirstIsland()
    {
        var mapDirectory = Path.Combine(_rootPath, "MapFile");
        var packDirectory = Path.Combine(_rootPath, "Pack");
        var modelDirectory = Path.Combine(_rootPath, "Model");

        var mapObjectPath = Directory
            .GetFiles(mapDirectory, "FirstIsland*_MapGrid_MapObject.byml", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(mapObjectPath))
        {
            throw new FileNotFoundException("Could not find a FirstIsland map object file.");
        }

        var mapGridPath = mapObjectPath.Replace("_MapObject.byml", "_MapGrid.byml", StringComparison.OrdinalIgnoreCase);
        if (!File.Exists(mapGridPath))
        {
            throw new FileNotFoundException($"Map grid file not found for {Path.GetFileName(mapObjectPath)}.");
        }

        var firstPackPath = Directory
            .GetFiles(packDirectory, "*.pack.zs", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(firstPackPath))
        {
            throw new FileNotFoundException("Could not find a top-level .pack.zs file in Pack.");
        }

        var mapBytes = File.ReadAllBytes(mapObjectPath);
        var tokens = BinaryStringScanner.ExtractAsciiStrings(mapBytes, 4)
            .Select(s => s.Trim())
            .Where(s => TokenRegex.IsMatch(s))
            .Where(s => !s.StartsWith("Object_", StringComparison.OrdinalIgnoreCase))
            .Where(s => !s.StartsWith("GridPos", StringComparison.OrdinalIgnoreCase))
            .Where(s => !s.Equals("root", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var models = FindCandidateModels(modelDirectory, tokens);
        if (models.Count == 0)
        {
            models = Directory.GetFiles(modelDirectory, "*.bfres.zs", SearchOption.TopDirectoryOnly)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Take(30)
                .ToList();
        }

        return new MapDiscoveryResult
        {
            MapGridPath = mapGridPath,
            MapObjectPath = mapObjectPath,
            FirstPackPath = firstPackPath,
            CandidateModelPaths = models
        };
    }

    private static List<string> FindCandidateModels(string modelDirectory, IReadOnlyCollection<string> tokens)
    {
        var allModels = Directory.GetFiles(modelDirectory, "*.bfres.zs", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (tokens.Count == 0 || allModels.Length == 0)
        {
            return [];
        }

        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var modelNames = allModels
            .Select(path => new
            {
                Path = path,
                Name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path))
            })
            .ToArray();

        foreach (var token in tokens)
        {
            foreach (var model in modelNames.Where(x =>
                         x.Name.Equals(token, StringComparison.OrdinalIgnoreCase) ||
                         x.Name.StartsWith(token, StringComparison.OrdinalIgnoreCase) ||
                         x.Name.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                selected.Add(model.Path);
                if (selected.Count >= 120)
                {
                    return selected.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
                }
            }
        }

        return selected.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
