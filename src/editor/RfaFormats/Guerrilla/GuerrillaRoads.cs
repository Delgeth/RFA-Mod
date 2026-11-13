using System.Numerics;
using System.Text;
using RfaFormats.Meshes;
using RfaFormats.Zones;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Red Faction: Guerrilla's road decals (the road meshes its sub-zones lay into the ground's
/// cut-outs; see <see cref="TerrainSubzone"/>) made into Armageddon's, for our sub-zones of its world:
///   the record (40 bytes: instance count, run-time words, and the position its vertices are from,
///     the zone's centre) as it is: Armageddon's is the same
///   the mesh block from version 6 to 9 (<see cref="MeshBlockConversion"/>), its vertex format 25
///     made 36, the same 24 bytes (position, normal, tangent, texture coordinates); its g-data as it
///     is but for each vertex's normal w (byte 15), how much of the road shows there, which both games'
///     shaders take as w × 2 − 1: Guerrilla's is 127 at the road's ends and joins (none: its shader
///     clamps the −0.004 to 0) and 129 to 255 elsewhere; Armageddon's road shader doesn't clamp it, so
///     those below 128 are made 128
///   the texture name list with each name renamed (the ground's layers and the road's textures
///     under their new names, the zone's blend map ours), each name followed by two zeros and padded
///     to an even length, as both games' are
///   the material map and material written from code (<see cref="TerrainMaterials.Road"/>), with the
///     shader for Guerrilla's own (its rl_terrain_road_layer2, 3 or 4: <see cref="TerrainMaterials.RoadShaderName"/>):
///     Armageddon's road shaders have the same nine texture slots as Guerrilla's and, by name hash,
///     the same constants but one Guerrilla names otherwise and the zone's centre (26CE49CD), which
///     Armageddon's haven't; each slot is given its texture's name and each constant Guerrilla's value.
///     So each road is drawn with the zone's own three ground layers and blend map, as in Guerrilla.
/// </summary>
public static class GuerrillaRoads
{
    const byte GuerrillaVertexFormat = 25, VertexFormat = 36;

    /// <summary>Guerrilla's Terrain_layer_spec_power (each layer's shine power), which Armageddon's road shaders name Specular_Power.</summary>
    const uint GuerrillaSpecularPower = 0x984BB1F3;

    /// <summary>Guerrilla's road shaders by their name hash: the variant's number.</summary>
    static readonly Dictionary<uint, int> GuerrillaVariants = new[] { 2, 3, 4 }.ToDictionary(v => NameHashes.PropertyHash($"rl_terrain_road_layer{v}"), v => v);

    /// <summary>Road decals converted so far by shader, and their vertices, and of them those at the road's ends and joins (made 128), for the builder's log.</summary>
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> Variants = new();
    public static long VerticesSeen, VerticesRaised;

    /// <summary>A road decal as Armageddon's sub-zones hold it, with <paramref name="rename"/> giving each texture's new name (without ".tga").</summary>
    public static RoadDecalToWrite Convert(GuerrillaRoadDecal decal, Func<string, string> rename)
    {
        byte[] mesh = MeshBlockConversion.FromVersion6(decal.MeshBlock, 0);
        if (mesh[0x21] != GuerrillaVertexFormat) throw new InvalidDataException($"a road decal with vertex format {mesh[0x21]}, not {GuerrillaVertexFormat}");
        mesh[0x21] = VertexFormat;
        if (!GuerrillaVariants.TryGetValue(decal.Shader, out int variant))
            throw new InvalidDataException($"a road decal with shader {decal.Shader:X8}, not one of Guerrilla's rl_terrain_road_layer2, 3 or 4");
        string shader = TerrainMaterials.RoadShaderName(variant);
        Variants.AddOrUpdate(shader, 1, (_, n) => n + 1);

        // The names, and where each starts (by Guerrilla's name).
        var names = new MemoryStream();
        var nameAt = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in decal.Names.Where(n => !nameAt.ContainsKey(n)))
        {
            nameAt[name] = (int)names.Length;
            names.Write(Encoding.ASCII.GetBytes(rename(Path.GetFileNameWithoutExtension(name)) + ".tga"));
            names.Write([0, 0]);
            if (names.Length % 2 != 0) names.WriteByte(0);
        }
        byte[] list = [.. BitConverter.GetBytes((int)names.Length), .. names.ToArray()];

        // The material: each slot given the texture Guerrilla's slot of the same hash has, each constant
        // Guerrilla's value.
        int[] textures = [.. TerrainMaterials.RoadSlots.Select(slot => nameAt[decal.Slots.FirstOrDefault(s => s.Slot == slot).Texture
                             ?? throw new InvalidDataException($"Guerrilla's road decal has no texture for slot {slot:X8}")])];
        Vector4 Constant(uint hash) => decal.Constants.TryGetValue(hash, out Vector4 value) ? value
                                       : throw new InvalidDataException($"Guerrilla's road decal has no constant {hash:X8}");
        Vector4 Named(string name) => Constant(NameHashes.PropertyHash(name));
        byte[] material = TerrainMaterials.Road(textures, [.. Enumerable.Range(0, 3).Select(k => Named($"Terrain_layer{k}_scale_offset"))],
            [.. Enumerable.Range(0, 4).Select(k => Named($"Diffuse_color_{k}"))], Constant(GuerrillaSpecularPower),
            Constant(0xE3218939), Constant(0xB92F865F), Constant(0x57CB7618), NameHashes.PropertyHash(shader));

        return new RoadDecalToWrite(decal.Record, mesh, BitConverter.ToInt32(decal.Record, 0), list, TerrainMaterials.RoadMap(), material,
            WithArmageddonWeights(mesh, decal.MeshData));
    }

    /// <summary>The mesh's g-data with each vertex's normal w (how much of the road shows) at least 128, none as Armageddon reads it (see the top).</summary>
    static byte[] WithArmageddonWeights(byte[] mesh, byte[] data)
    {
        const int NormalW = 15;  // after the position (3 floats) and the normal's x, y, z
        int vertices = BitConverter.ToInt32(mesh, 0x1C), size = mesh[0x20], first = BitConverter.ToInt32(mesh, 0x3C);
        if (size < NormalW + 1 || first < 0 || first + (long)vertices * size > data.Length)
            throw new InvalidDataException($"a road decal's {vertices} vertices of {size} bytes from {first} don't fit its {data.Length} bytes");
        byte[] made = (byte[])data.Clone();
        long raised = 0;
        for (int v = 0; v < vertices; v++)
        {
            int at = first + v * size + NormalW;
            if (made[at] >= 128) continue;
            made[at] = 128;
            raised++;
        }
        Interlocked.Add(ref VerticesSeen, vertices);
        Interlocked.Add(ref VerticesRaised, raised);
        return made;
    }
}
