using System.Numerics;
using System.Text.Json.Nodes;
using Aidyn.Rom.Graphics;

namespace Aidyn.Rom.Maps;

/// <summary>
/// Exports a map set's collision as glTF: a node per chunk at
/// its <see cref="MapSets.ChunkOrigin"/>, with one mesh of its triangles. The triangles don't share
/// vertices, so each vertex carries its triangle's <c>_SURFACE</c> (<see cref="CollisionSurface"/>),
/// <c>_ROLE</c> (0 normal, 1 toggle, 2 trigger, 3 Expansion Pak only) and <c>_FLAGS</c>
/// (<see cref="TriangleFlags"/>). With no normals, viewers shade them flat.
/// </summary>
/// <remarks>The importer exports each chunk alone, in its own coordinates, with <c>placeInSet</c> off.</remarks>
public static class CollisionGltf
{
    public static byte[] Export(IEnumerable<(MapChunk Chunk, Borg9Map Map)> chunks, bool placeInSet = true)
    {
        var gltf = new GltfBuilder();
        var material = gltf.AddMaterial(new JsonObject
        {
            ["name"] = "collision",
            ["doubleSided"] = true,
            ["pbrMetallicRoughness"] = new JsonObject { ["metallicFactor"] = 0, ["roughnessFactor"] = 1 },
        });

        var roots = new List<int>();
        foreach (var (chunk, map) in chunks)
        {
            var node = new JsonObject
            {
                ["name"] = $"{chunk.Name} borg9 {chunk.Collision}",
                ["extras"] = new JsonObject { ["chunk"] = chunk.Name, ["borg9"] = chunk.Collision },
            };
            var origin = placeInSet ? MapSets.ChunkOrigin(chunk) : Vector3.Zero;
            if (origin != Vector3.Zero)
                node["translation"] = new JsonArray(origin.X, origin.Y, origin.Z);

            if (map.Triangles.Count > 0)
            {
                var positions = new List<Vector3>();
                List<float> surfaces = [], roles = [], flags = [], targets = [];
                foreach (var t in map.Triangles)
                {
                    positions.AddRange([map.Vertices[t.A], map.Vertices[t.B], map.Vertices[t.C]]);
                    var role = t.Role switch
                    {
                        TriangleRole.Toggle => 1, TriangleRole.Trigger => 2, TriangleRole.ExpansionPak => 3, _ => 0,
                    };
                    for (var i = 0; i < 3; i++)
                    {
                        surfaces.Add((float)t.Surface);
                        roles.Add(role);
                        flags.Add((float)t.Flags);
                        targets.Add(t.ToggleId ?? t.TriggerObject ?? 0);
                    }
                }
                node["mesh"] = gltf.AddMesh(new JsonObject
                {
                    ["name"] = $"collision {chunk.Collision}",
                    ["primitives"] = new JsonArray(new JsonObject
                    {
                        ["attributes"] = new JsonObject
                        {
                            ["POSITION"] = gltf.AddPositions(positions),
                            ["_SURFACE"] = gltf.AddScalars(surfaces),
                            ["_ROLE"] = gltf.AddScalars(roles),
                            ["_FLAGS"] = gltf.AddScalars(flags),
                            ["_TARGET"] = gltf.AddScalars(targets),
                        },
                        ["material"] = material,
                    }),
                });
            }
            roots.Add(gltf.AddNode(node));
        }
        return gltf.ToGlb(roots);
    }
}
