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
///     made 36, the same 24 bytes (position, normal, tangent, texture coordinates); its g-data as it is
///   the texture name list with each name renamed (the ground's layers and the road's textures
///     under their new names, the zone's blend map ours), each name followed by two zeros and padded
///     to an even length, as both games' are
///   the material map and material written from code (<see cref="TerrainMaterials.Road"/>):
///     Armageddon's road shader has the same nine texture slots as Guerrilla's and, by name hash,
///     the same constants but one Guerrilla names otherwise and the zone's centre (26CE49CD), which
///     Armageddon's hasn't; each slot is given its texture's name and each constant Guerrilla's value.
/// </summary>
public static class GuerrillaRoads
{
    const byte GuerrillaVertexFormat = 25, VertexFormat = 36;

    /// <summary>Guerrilla's name hash for Armageddon's Specular_Power (the value (1, 1, 1, 50) in both).</summary>
    const uint GuerrillaSpecularPower = 0x984BB1F3;

    /// <summary>A road decal as Armageddon's sub-zones hold it, with <paramref name="rename"/> giving each texture's new name (without ".tga").</summary>
    public static RoadDecalToWrite Convert(GuerrillaRoadDecal decal, Func<string, string> rename)
    {
        byte[] mesh = MeshBlockConversion.FromVersion6(decal.MeshBlock, 0);
        if (mesh[0x21] != GuerrillaVertexFormat) throw new InvalidDataException($"a road decal with vertex format {mesh[0x21]}, not {GuerrillaVertexFormat}");
        mesh[0x21] = VertexFormat;

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
            Constant(0xE3218939), Constant(0xB92F865F), Constant(0x57CB7618));

        return new RoadDecalToWrite(decal.Record, mesh, BitConverter.ToInt32(decal.Record, 0), list, TerrainMaterials.RoadMap(), material, decal.MeshData);
    }
}
