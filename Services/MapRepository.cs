using System.Text.Json;
using System.Text;
using BymlLibrary;
using MapIslandEditor.Models;
using Revrs;

namespace MapIslandEditor.Services;

public sealed class MapRepository
{
    private readonly string _rootPath;
    private readonly string _mapPath;

    public MapRepository(string rootPath)
    {
        _rootPath = rootPath;
        _mapPath = Path.Combine(rootPath, "MapFile");
    }

    public InitialMapSystemInfo LoadInitialMapSystemInfo()
    {
        var path = Path.Combine(_rootPath, "Parameter", "InitialMapSystem", "System.game__InitialMapSystem.bgyml");
        if (!File.Exists(path))
        {
            return new InitialMapSystemInfo();
        }

        var byml = Byml.FromBinary(File.ReadAllBytes(path), out _, out _);
        if (!TryGetRootNode(byml, out var rootNode))
        {
            return new InitialMapSystemInfo();
        }

        var rootMap = rootNode.GetMap();

        return new InitialMapSystemInfo
        {
            InitialMapFileHash = rootMap.TryGetValue("InitialMapFileHash", out var hashNode) ? GetUInt(hashNode) : 0u,
            IsAvailableIntroduction = rootMap.TryGetValue("IsAvailableIntroduction", out var introNode) && GetBool(introNode)
        };
    }

    public Dictionary<uint, List<string>> LoadObjectNameLookup()
    {
        var lookup = new Dictionary<uint, List<string>>();
        var analysisPath = Path.Combine(_rootPath, "_map_editor_research", "map_analysis_csharp.json");
        if (!File.Exists(analysisPath))
        {
            analysisPath = Path.Combine(_rootPath, "_map_editor_research", "map_analysis.json");
        }

        if (File.Exists(analysisPath))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(analysisPath));
            if (doc.RootElement.TryGetProperty("objectHashLookup", out var objectLookup) && objectLookup.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in objectLookup.EnumerateObject())
                {
                    if (!uint.TryParse(property.Name, out var hash))
                    {
                        continue;
                    }

                    if (property.Value.ValueKind != JsonValueKind.Array)
                    {
                        continue;
                    }

                    var names = new List<string>();
                    foreach (var n in property.Value.EnumerateArray())
                    {
                        if (n.ValueKind == JsonValueKind.String)
                        {
                            var value = n.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                names.Add(value);
                            }
                        }
                    }

                    if (names.Count > 0)
                    {
                        lookup[hash] = names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
                    }
                }    
            }
        }

        foreach (var kv in BuildActorHashLookup())
        {
            if (lookup.TryGetValue(kv.Key, out var existing))
            {
                var merged = existing.Concat(kv.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                lookup[kv.Key] = merged;
            }
            else
            {
                lookup[kv.Key] = kv.Value;
            }
        }

        return lookup;
    }

    public Dictionary<uint, List<string>> LoadFloorNameLookup()
    {
        var lookup = new Dictionary<uint, List<string>>();
        var analysisPath = Path.Combine(_rootPath, "_map_editor_research", "map_analysis_csharp.json");
        if (!File.Exists(analysisPath))
        {
            analysisPath = Path.Combine(_rootPath, "_map_editor_research", "map_analysis.json");
        }

        if (!File.Exists(analysisPath))
        {
            return lookup;
        }

        using var doc = JsonDocument.Parse(File.ReadAllText(analysisPath));
        if (!doc.RootElement.TryGetProperty("maps", out var mapsNode) || mapsNode.ValueKind != JsonValueKind.Array)
        {
            return lookup;
        }

        foreach (var mapNode in mapsNode.EnumerateArray())
        {
            if (!mapNode.TryGetProperty("floorHashRows", out var floorRows) || floorRows.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var floorEntry in floorRows.EnumerateObject())
            {
                if (!uint.TryParse(floorEntry.Name, out var hash) || floorEntry.Value.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                if (!lookup.TryGetValue(hash, out var names))
                {
                    names = [];
                    lookup[hash] = names;
                }

                foreach (var floorPathNode in floorEntry.Value.EnumerateArray())
                {
                    if (floorPathNode.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var floorPath = floorPathNode.GetString();
                    if (string.IsNullOrWhiteSpace(floorPath))
                    {
                        continue;
                    }

                    var floorName = ExtractFloorNameFromPath(floorPath);
                    if (string.IsNullOrWhiteSpace(floorName))
                    {
                        continue;
                    }

                    if (!names.Any(n => n.Equals(floorName, StringComparison.OrdinalIgnoreCase)))
                    {
                        names.Add(floorName);
                    }
                }
            }
        }

        foreach (var kv in lookup)
        {
            kv.Value.Sort(StringComparer.OrdinalIgnoreCase);
        }

        return lookup;
    }

    public List<MapProject> LoadAllMaps()
    {
        if (!Directory.Exists(_mapPath))
        {
            return [];
        }

        var gridFiles = Directory.GetFiles(_mapPath, "*_MapGrid.byml", SearchOption.TopDirectoryOnly)
            .Concat(Directory.GetFiles(_mapPath, "*_MapGrid_MapGrid.byml", SearchOption.TopDirectoryOnly))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)
            .ToList();

        var maps = new List<MapProject>();
        foreach (var gridFile in gridFiles)
        {
            var gridName = Path.GetFileName(gridFile);
            var objectName = gridName.Replace("_MapGrid_MapGrid.byml", "_MapGrid_MapObject.byml", StringComparison.Ordinal)
                .Replace("_MapGrid.byml", "_MapObject.byml", StringComparison.Ordinal);
            var objectFile = Path.Combine(_mapPath, objectName);
            if (!File.Exists(objectFile))
            {
                continue;
            }

            var map = LoadMap(gridFile, objectFile);
            if (map is not null)
            {
                maps.Add(map);
            }
        }

        return maps;
    }

    public MapProject? LoadMap(string gridFile, string objectFile)
    {
        if (!File.Exists(gridFile) || !File.Exists(objectFile))
        {
            return null;
        }

        var gridName = Path.GetFileName(gridFile);
        var gridByml = Byml.FromBinary(File.ReadAllBytes(gridFile), out var gridEndianness, out var gridVersion);
        var objectByml = Byml.FromBinary(File.ReadAllBytes(objectFile), out var objectEndianness, out var objectVersion);

        if (!TryGetRootNode(gridByml, out var gridRootNode))
        {
            return null;
        }

        var gridRoot = gridRootNode.GetMap();
        if (!gridRoot.TryGetValue("Grid", out var gridNode) || gridNode.Type.ToString() != "Array")
        {
            return null;
        }

        var gridRows = gridNode.GetArray();
        var height = gridRows.Count;
        var width = height > 0 ? gridRows[0].GetArray().Count : 0;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var grid = new uint[width, height];
        for (var y = 0; y < height; y++)
        {
            var row = gridRows[y].GetArray();
            for (var x = 0; x < width; x++)
            {
                grid[x, y] = GetUInt(row[x]);
            }
        }

        var ugc = new uint[width, height];
        if (gridRoot.TryGetValue("UgcFloor", out var ugcNode) && ugcNode.Type.ToString() == "Array")
        {
            var ugcRows = ugcNode.GetArray();
            for (var y = 0; y < Math.Min(height, ugcRows.Count); y++)
            {
                var row = ugcRows[y].GetArray();
                for (var x = 0; x < Math.Min(width, row.Count); x++)
                {
                    ugc[x, y] = GetUInt(row[x]);
                }
            }
        }

        uint[,]? invalid = null;
        if (gridRoot.TryGetValue("InvalidGridFlag", out var invalidNode) && invalidNode.Type.ToString() == "Array")
        {
            invalid = new uint[width, height];
            var invalidRows = invalidNode.GetArray();
            for (var y = 0; y < Math.Min(height, invalidRows.Count); y++)
            {
                var row = invalidRows[y].GetArray();
                for (var x = 0; x < Math.Min(width, row.Count); x++)
                {
                    invalid[x, y] = GetUInt(row[x]);
                }
            }
        }

        var objects = ReadObjects(objectByml);
        return new MapProject
        {
            Name = gridName.Replace("_MapGrid_MapGrid.byml", string.Empty, StringComparison.Ordinal).Replace("_MapGrid.byml", string.Empty, StringComparison.Ordinal),
            MapKey = GetMapKey(gridName),
            GridFilePath = gridFile,
            ObjectFilePath = objectFile,
            Width = width,
            Height = height,
            Grid = grid,
            UgcFloor = ugc,
            InvalidGridFlag = invalid,
            GridSizeType = gridRoot.TryGetValue("GridSizeType", out var sizeType) ? sizeType.GetString() : string.Empty,
            CanBeFocus = gridRoot.TryGetValue("CanBeFocus", out var canFocus) && GetBool(canFocus),
            CanEnterSequence = gridRoot.TryGetValue("CanEnterSequence", out var canEnter) && GetBool(canEnter),
            HasInvalidGridFlag = gridRoot.ContainsKey("InvalidGridFlag"),
            GridEndianness = gridEndianness,
            GridVersion = gridVersion,
            ObjectEndianness = objectEndianness,
            ObjectVersion = objectVersion,
            Objects = objects
        };
    }

    public void SaveMap(MapProject map)
    {
        SaveMap(map, map.GridFilePath, map.ObjectFilePath);
    }

    public void SaveMap(MapProject map, string gridFilePath, string objectFilePath)
    {
        var gridTop = new Dictionary<string, Byml>
        {
            ["root"] = BuildGridRoot(map)
        };

        var objectTop = new Dictionary<string, Byml>
        {
            ["root"] = BuildObjectRoot(map)
        };

        var gridByml = new Byml(gridTop);
        var objectByml = new Byml(objectTop);

        var gridBytes = gridByml.ToBinary(map.GridEndianness, map.GridVersion);
        var objectBytes = objectByml.ToBinary(map.ObjectEndianness, map.ObjectVersion);

        File.WriteAllBytes(gridFilePath, gridBytes);
        File.WriteAllBytes(objectFilePath, objectBytes);
    }

    private static List<MapObjectEntry> ReadObjects(Byml objectByml)
    {
        var objects = new List<MapObjectEntry>();
        if (!TryGetRootNode(objectByml, out var rootNode))
        {
            return objects;
        }

        var root = rootNode.GetMap();

        foreach (var kv in root.OrderBy(k => ParseObjectIndex(k.Key)))
        {
            if (kv.Value.Type.ToString() != "Map")
            {
                continue;
            }

            var map = kv.Value.GetMap();
            if (!map.TryGetValue("Hash", out var hashNode))
            {
                continue;
            }

            var x = 0;
            var y = 0;
            var rotY = 0f;

            if (map.TryGetValue("Location", out var locationNode) && locationNode.Type.ToString() == "Map")
            {
                var location = locationNode.GetMap();
                if (location.TryGetValue("GridPos", out var gridPosNode) && gridPosNode.Type.ToString() == "Map")
                {
                    var gridPos = gridPosNode.GetMap();
                    if (gridPos.TryGetValue("GridPosX", out var gx))
                    {
                        x = GetInt(gx);
                    }

                    if (gridPos.TryGetValue("GridPosY", out var gy))
                    {
                        y = GetInt(gy);
                    }
                }
            }

            if (map.TryGetValue("MapObjectMisc", out var miscNode) && miscNode.Type.ToString() == "Map")
            {
                var misc = miscNode.GetMap();
                if (misc.TryGetValue("RotY", out var rotNode))
                {
                    rotY = GetFloat(rotNode);
                }
            }

            objects.Add(new MapObjectEntry
            {
                Id = kv.Key,
                Hash = GetUInt(hashNode),
                GridPosX = x,
                GridPosY = y,
                RotY = rotY
            });
        }

        return objects;
    }

    private static bool TryGetRootNode(Byml byml, out Byml rootNode)
    {
        rootNode = new Byml();
        if (byml.Type.ToString() != "Map")
        {
            return false;
        }

        var map = byml.GetMap();
        if (map.TryGetValue("root", out var explicitRootNode) && explicitRootNode.Type.ToString() == "Map")
        {
            rootNode = explicitRootNode;
            return true;
        }

        rootNode = byml;
        return true;
    }

    private Dictionary<uint, List<string>> BuildActorHashLookup()
    {
        var result = new Dictionary<uint, List<string>>();
        var actorDir = Path.Combine(_rootPath, "Actor");
        if (!Directory.Exists(actorDir))
        {
            return result;
        }

        var files = Directory.GetFiles(actorDir, "*.actor__ActorParam.gyml", SearchOption.TopDirectoryOnly);
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var token = name.Replace(".actor__ActorParam.gyml", string.Empty, StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(token))
            {
                continue;
            }

            var baseToken = token;
            var lastUnderscore = token.LastIndexOf('_');
            if (lastUnderscore > 0 && int.TryParse(token[(lastUnderscore + 1)..], out _))
            {
                baseToken = token[..lastUnderscore];
            }

            var hash = MurmurHash3X86_32(baseToken);
            if (!result.TryGetValue(hash, out var list))
            {
                list = [];
                result[hash] = list;
            }

            if (!list.Any(s => s.Equals(baseToken, StringComparison.OrdinalIgnoreCase)))
            {
                list.Add(baseToken);
            }
        }

        foreach (var kv in result)
        {
            kv.Value.Sort(StringComparer.OrdinalIgnoreCase);
        }

        return result;
    }

    private static string GetMapKey(string gridFileName)
    {
        if (gridFileName.EndsWith("_MapGrid_MapGrid.byml", StringComparison.Ordinal))
        {
            return gridFileName[..^"_MapGrid_MapGrid.byml".Length] + "_MapGrid";
        }

        if (gridFileName.EndsWith("_MapGrid.byml", StringComparison.Ordinal))
        {
            return gridFileName[..^"_MapGrid.byml".Length] + "_MapGrid";
        }

        return Path.GetFileNameWithoutExtension(gridFileName);
    }

    private static int ParseObjectIndex(string id)
    {
        if (id.StartsWith("Object_", StringComparison.Ordinal) && int.TryParse(id[7..], out var n))
        {
            return n;
        }

        return int.MaxValue;
    }

    private static Byml BuildGridRoot(MapProject map)
    {
        var root = new Dictionary<string, Byml>
        {
            ["CanBeFocus"] = new Byml(map.CanBeFocus),
            ["CanEnterSequence"] = new Byml(map.CanEnterSequence),
            ["Grid"] = Build2DUIntArray(map.Grid),
            ["GridNumX"] = new Byml(map.Width),
            ["GridNumZ"] = new Byml(map.Height),
            ["GridSizeType"] = new Byml(map.GridSizeType),
            ["UgcFloor"] = Build2DUIntArray(map.UgcFloor)
        };

        if (map.InvalidGridFlag is not null)
        {
            root["InvalidGridFlag"] = Build2DUIntArray(map.InvalidGridFlag);
        }

        return new Byml(root);
    }

    private static Byml BuildObjectRoot(MapProject map)
    {
        var root = new Dictionary<string, Byml>();
        var ordered = map.Objects
            .OrderBy(o => ParseObjectIndex(o.Id))
            .ThenBy(o => o.Id, StringComparer.Ordinal)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var o = ordered[i];
            var id = $"Object_{i}";
            var entry = new Dictionary<string, Byml>
            {
                ["Hash"] = new Byml(o.Hash),
                ["Location"] = new Byml(new Dictionary<string, Byml>
                {
                    ["GridPos"] = new Byml(new Dictionary<string, Byml>
                    {
                        ["GridPosX"] = new Byml(o.GridPosX),
                        ["GridPosY"] = new Byml(o.GridPosY)
                    })
                }),
                ["MapObjectMisc"] = new Byml(new Dictionary<string, Byml>
                {
                    ["RotY"] = new Byml(o.RotY)
                })
            };
            root[id] = new Byml(entry);
        }

        return new Byml(root);
    }

    private static Byml Build2DUIntArray(uint[,] data)
    {
        var width = data.GetLength(0);
        var height = data.GetLength(1);
        var rows = new Byml[height];

        for (var y = 0; y < height; y++)
        {
            var row = new Byml[width];
            for (var x = 0; x < width; x++)
            {
                row[x] = new Byml(data[x, y]);
            }
            rows[y] = new Byml(row);
        }

        return new Byml(rows);
    }

    private static bool GetBool(Byml node)
    {
        return node.Type.ToString() == "Bool" && node.GetBool();
    }

    private static float GetFloat(Byml node)
    {
        var type = node.Type.ToString();
        return type switch
        {
            "Float" => node.GetFloat(),
            "Double" => (float)node.GetDouble(),
            "Int" => node.GetInt(),
            "UInt32" => node.GetUInt32(),
            _ => 0f
        };
    }

    private static int GetInt(Byml node)
    {
        var type = node.Type.ToString();
        return type switch
        {
            "Int" => node.GetInt(),
            "UInt32" => unchecked((int)node.GetUInt32()),
            _ => 0
        };
    }

    private static uint GetUInt(Byml node)
    {
        var type = node.Type.ToString();
        return type switch
        {
            "UInt32" => node.GetUInt32(),
            "Int" => unchecked((uint)node.GetInt()),
            _ => 0u
        };
    }

    private static uint MurmurHash3X86_32(string text, uint seed = 0)
    {
        var data = Encoding.UTF8.GetBytes(text);
        const uint c1 = 0xcc9e2d51;
        const uint c2 = 0x1b873593;
        uint h1 = seed;
        var roundedEnd = data.Length & ~0x3;

        for (var i = 0; i < roundedEnd; i += 4)
        {
            uint k1 = (uint)(data[i] | data[i + 1] << 8 | data[i + 2] << 16 | data[i + 3] << 24);
            k1 *= c1;
            k1 = RotateLeft(k1, 15);
            k1 *= c2;

            h1 ^= k1;
            h1 = RotateLeft(h1, 13);
            h1 = h1 * 5 + 0xe6546b64;
        }

        uint k2 = 0;
        switch (data.Length & 3)
        {
            case 3:
                k2 ^= (uint)data[roundedEnd + 2] << 16;
                goto case 2;
            case 2:
                k2 ^= (uint)data[roundedEnd + 1] << 8;
                goto case 1;
            case 1:
                k2 ^= data[roundedEnd];
                k2 *= c1;
                k2 = RotateLeft(k2, 15);
                k2 *= c2;
                h1 ^= k2;
                break;
        }

        h1 ^= (uint)data.Length;
        h1 ^= h1 >> 16;
        h1 *= 0x85ebca6b;
        h1 ^= h1 >> 13;
        h1 *= 0xc2b2ae35;
        h1 ^= h1 >> 16;
        return h1;
    }

    private static uint RotateLeft(uint value, int count)
    {
        return (value << count) | (value >> (32 - count));
    }

    private static string ExtractFloorNameFromPath(string floorPath)
    {
        var fileName = Path.GetFileName(floorPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return string.Empty;
        }

        var token = fileName
            .Replace(".actor__FloorSetting.gyml", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("FloorSetting", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim('_', '-', ' ');

        return token;
    }
}
