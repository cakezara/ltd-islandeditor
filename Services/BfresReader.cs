using System.Numerics;
using System.Reflection;
using MapIslandEditor.Models;
using Syroot.NintenTools.Bfres;
using Syroot.NintenTools.Bfres.Helpers;

namespace MapIslandEditor.Services;

public sealed class BfresReader
{
    private readonly ZsDecompressionService _decompressor = new();

    public BfresMeshData ReadMesh(string modelPath)
    {
        var bytes = _decompressor.ReadPossiblyCompressed(modelPath);
        using var stream = new MemoryStream(bytes);
        var resFile = new ResFile(stream, false);
        var model = resFile.Models.Values.FirstOrDefault();
        if (model is null)
        {
            return EmptyMesh(modelPath);
        }

        var positionsByVertexBuffer = ReadPositionsByVertexBuffer(model);
        if (positionsByVertexBuffer.Count == 0)
        {
            return EmptyMesh(modelPath);
        }

        var positions = new List<float>();
        var indices = new List<int>();

        foreach (var shape in model.Shapes.Values)
        {
            if (!positionsByVertexBuffer.TryGetValue(shape.VertexBufferIndex, out var sourceVerts) || sourceVerts.Length == 0)
            {
                continue;
            }

            foreach (var mesh in shape.Meshes)
            {
                var rawIndices = mesh.GetIndices().ToArray();
                if (rawIndices.Length < 3)
                {
                    continue;
                }

                var baseVertex = positions.Count / 3;
                foreach (var v in sourceVerts)
                {
                    positions.Add(v.X);
                    positions.Add(v.Y);
                    positions.Add(v.Z);
                }

                for (var i = 0; i + 2 < rawIndices.Length; i += 3)
                {
                    var ia = (int)rawIndices[i];
                    var ib = (int)rawIndices[i + 1];
                    var ic = (int)rawIndices[i + 2];
                    if ((uint)ia >= (uint)sourceVerts.Length || (uint)ib >= (uint)sourceVerts.Length || (uint)ic >= (uint)sourceVerts.Length)
                    {
                        continue;
                    }

                    indices.Add(baseVertex + ia);
                    indices.Add(baseVertex + ib);
                    indices.Add(baseVertex + ic);
                }
            }
        }

        if (positions.Count == 0 || indices.Count == 0)
        {
            return EmptyMesh(modelPath);
        }

        return new BfresMeshData
        {
            SourcePath = modelPath,
            DisplayName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(modelPath)),
            Positions = positions,
            Indices = indices
        };
    }

    private static Dictionary<int, Vector3[]> ReadPositionsByVertexBuffer(Syroot.NintenTools.Bfres.Model model)
    {
        var result = new Dictionary<int, Vector3[]>();
        for (var i = 0; i < model.VertexBuffers.Count; i++)
        {
            var helper = new VertexBufferHelper(model.VertexBuffers[i], null);
            var attribute = helper.Attributes.FirstOrDefault(a =>
                string.Equals(a.Name, "_p0", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(a.Name, "Position", StringComparison.OrdinalIgnoreCase));

            if (attribute is null)
            {
                continue;
            }

            var dataField = attribute.GetType().GetField("Data", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (dataField?.GetValue(attribute) is not Array values || values.Length == 0)
            {
                continue;
            }

            var verts = new Vector3[values.Length];
            for (var v = 0; v < values.Length; v++)
            {
                var vec = values.GetValue(v);
                if (vec is null)
                {
                    continue;
                }

                var t = vec.GetType();
                var x = Convert.ToSingle(t.GetField("X")?.GetValue(vec) ?? 0f);
                var y = Convert.ToSingle(t.GetField("Y")?.GetValue(vec) ?? 0f);
                var z = Convert.ToSingle(t.GetField("Z")?.GetValue(vec) ?? 0f);
                verts[v] = new Vector3(x, y, z);
            }

            result[i] = verts;
        }

        return result;
    }

    private static BfresMeshData EmptyMesh(string modelPath)
    {
        return new BfresMeshData
        {
            SourcePath = modelPath,
            DisplayName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(modelPath)),
            Positions = [],
            Indices = []
        };
    }
}
