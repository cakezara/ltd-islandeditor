namespace MapIslandEditor.Services;

public sealed class ModelFileResolver
{
    private readonly List<string> _modelPaths;

    public ModelFileResolver(string rootPath)
    {
        var modelDir = Directory.Exists(Path.Combine(rootPath, "Model"))
            ? Path.Combine(rootPath, "Model")
            : rootPath;
        if (!Directory.Exists(modelDir))
        {
            _modelPaths = [];
            return;
        }

        _modelPaths = Directory.GetFiles(modelDir, "*.bfres*", SearchOption.AllDirectories)
            .Where(p =>
                p.EndsWith(".bfres", StringComparison.OrdinalIgnoreCase) ||
                p.EndsWith(".bfres.zs", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string? ResolveFromObjectName(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName) || objectName == "(unknown)" || _modelPaths.Count == 0)
        {
            return null;
        }

        var candidates = BuildCandidates(objectName);
        foreach (var candidate in candidates)
        {
            var match = _modelPaths.FirstOrDefault(p =>
            {
                var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(p));
                return name.Equals(candidate, StringComparison.OrdinalIgnoreCase);
            });

            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        foreach (var candidate in candidates)
        {
            var match = _modelPaths.FirstOrDefault(p =>
            {
                var name = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(p));
                return name.Contains(candidate, StringComparison.OrdinalIgnoreCase);
            });

            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return null;
    }

    private static List<string> BuildCandidates(string name)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            name.Trim()
        };

        var strippedNumeric = StripNumericSuffix(name);
        set.Add(strippedNumeric);

        if (name.StartsWith("Facility", StringComparison.OrdinalIgnoreCase))
        {
            set.Add(name[8..]);
            set.Add(StripNumericSuffix(name[8..]));
        }

        if (name.StartsWith("Obj", StringComparison.OrdinalIgnoreCase))
        {
            set.Add(name[3..]);
            set.Add(StripNumericSuffix(name[3..]));
        }

        var underscore = name.IndexOf('_');
        if (underscore > 0 && underscore < name.Length - 1)
        {
            set.Add(name[(underscore + 1)..]);
            set.Add(StripNumericSuffix(name[(underscore + 1)..]));
        }

        return set.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
    }

    private static string StripNumericSuffix(string value)
    {
        var idx = value.LastIndexOf('_');
        if (idx <= 0 || idx >= value.Length - 1)
        {
            return value;
        }

        var suffix = value[(idx + 1)..];
        return suffix.All(char.IsDigit) ? value[..idx] : value;
    }
}
