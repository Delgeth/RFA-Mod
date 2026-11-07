using System.Numerics;
using RfaFormats.Meshes;

namespace RfaFormats.Guerrilla;

/// <summary>
/// Guerrilla's faces baked into meshes of their own. Guerrilla draws most of its people's heads as a base
/// head (base_head, base_hires_head: an item's character mesh) shaped by the face's morph
/// (&lt;face&gt;.morph_pc in the character's container: one target, the face) at full weight. Armageddon kept
/// the head code but not that step (its morph instance takes only its own format), so the face is added to
/// the base head's vertices here, once, and the head is drawn as a mesh of its own (see GuerrillaPeople's heads).
///
/// The morph file (format 1, Guerrilla's sparse one):
///   +0x00 0x0BADBEEF, version 2, format 1, how many targets, (run-time word)
///   +0x14 a target each, 0x24 bytes: its name's hash, how many vertices it moves, the most it moves a
///         position (x, y, z, metres), the most it bends a normal (x, y, z), (run-time word)
///   then, from a 16-byte boundary, each target's vertices, 12 bytes each: the move (3 × int16, of 32,767
///   of its most), the base head's vertex number (u16: the whole vertex buffer, the eyes and every detail
///   level in it), the normal's bend (3 bytes, 127 none, × its most a byte of the normal), a pad byte.
/// (As Guerrilla's rl_dynamic_morph_instance::add_morph_contribution adds a target. Checked: Braun's and
/// Dustin's faces baked onto base_head lie 1.4 and 3.5 mm on average from their own low-detail heads, which
/// carry the face shape; the plain base head lies 11.5 and 12.3 mm from them.)
/// The vertices are Guerrilla's character format 3 (32 bytes): position (3 floats), normal (4 bytes, 0-255
/// = -1 to 1), tangent (4 bytes, w the bitangent's sign), bone weights, bone numbers, UV.
/// </summary>
public static class GuerrillaFaces
{
    const uint Magic = 0x0BADBEEF;
    const int TargetBytes = 0x24, MoveBytes = 12, CharacterFormat = 3;

    /// <summary>
    /// The base head's .gcmesh_pc with the face's first morph target added to its vertices (the rest of the file,
    /// its checksum words among it, as it is). <paramref name="cmesh"/> is the base head's Guerrilla .ccmesh_pc
    /// (version 3), whose mesh block says where the vertices are; <paramref name="report"/> gets what was moved.
    /// </summary>
    public static byte[] Bake(byte[] cmesh, byte[] gmesh, byte[] morph, out string report)
    {
        int I(byte[] b, int at) => BitConverter.ToInt32(b, at);
        if (morph.Length < 0x14 + TargetBytes || BitConverter.ToUInt32(morph, 0) != Magic) throw new InvalidDataException("not a morph file");
        if (I(morph, 4) != 2 || I(morph, 8) != 1) throw new InvalidDataException($"morph version {I(morph, 4)}, format {I(morph, 8)}: not Guerrilla's (2, 1)");
        int targets = I(morph, 12);
        if (targets < 1) throw new InvalidDataException("the morph has no target");

        var (vertexCount, vertexSize, vertexStart) = Vertices(cmesh, gmesh);

        // The first target's vertices: after every target's header, from a 16-byte boundary.
        int header = 0x14;
        int moves = I(morph, header + 4);
        var most = new Vector3(F(morph, header + 8), F(morph, header + 12), F(morph, header + 16));
        var bend = new Vector3(F(morph, header + 20), F(morph, header + 24), F(morph, header + 28));
        int at = (0x14 + TargetBytes * targets + 15) & ~15;
        if (at + moves * MoveBytes > morph.Length) throw new InvalidDataException("the morph's vertices run past its end");

        var baked = (byte[])gmesh.Clone();
        float largest = 0;
        for (int m = 0; m < moves; m++, at += MoveBytes)
        {
            var move = new Vector3(BitConverter.ToInt16(morph, at), BitConverter.ToInt16(morph, at + 2), BitConverter.ToInt16(morph, at + 4)) / 32767f * most;
            int vertex = BitConverter.ToUInt16(morph, at + 6);
            if (vertex >= vertexCount) throw new InvalidDataException($"the morph moves vertex {vertex} of {vertexCount}");
            int v = vertexStart + vertex * vertexSize;
            var position = new Vector3(F(baked, v), F(baked, v + 4), F(baked, v + 8)) + move;
            Put(baked, v, position.X);
            Put(baked, v + 4, position.Y);
            Put(baked, v + 8, position.Z);
            largest = Math.Max(largest, move.Length());

            // The normal: each byte bent by (the bend's byte − 127) × its most, then made unit length again;
            // the tangent turned to stay square to it.
            var normalBytes = new Vector3(baked[v + 12], baked[v + 13], baked[v + 14])
                            + new Vector3(morph[at + 8] - 127, morph[at + 9] - 127, morph[at + 10] - 127) * bend;
            var normal = Vector3.Normalize(normalBytes / 127.5f - Vector3.One);
            var tangent = new Vector3(baked[v + 16], baked[v + 17], baked[v + 18]) / 127.5f - Vector3.One;
            tangent -= normal * Vector3.Dot(normal, tangent);
            tangent = tangent.LengthSquared() > 1e-8f ? Vector3.Normalize(tangent) : tangent;
            Bytes(baked, v + 12, normal);
            Bytes(baked, v + 16, tangent);
        }
        report = $"{moves} of {vertexCount} vertices moved (up to {largest * 1000:0} mm)";
        return baked;
    }

    /// <summary>
    /// The head's .gcmesh_pc moved from the face's skeleton (<paramref name="face"/>, &lt;face&gt;_head.rig_pc) onto its
    /// base head's (<paramref name="baseHead"/>), so every face can share the base head's skeleton: the game holds
    /// at most 290 skeletons at once, and Guerrilla's faces have 32 of their own. A face's skeleton is the base head's
    /// five bones (spine2, neck, head, the eyes) in the same order, placed for that face (up to 49 mm from the base
    /// head's), and a rest pose has no rotations, so each vertex is moved by the difference of its bones' places,
    /// weighted as it is (exact for a vertex on one bone, as most are; a vertex shared by bones that turn apart, at the
    /// neck, lands within a millimetre or so). Normals and tangents don't change. <paramref name="largest"/> is the
    /// farthest a vertex moved.
    /// </summary>
    public static byte[] Rebase(byte[] cmesh, byte[] gmesh, Rig face, Rig baseHead, out float largest)
    {
        if (face.Bones.Count != baseHead.Bones.Count || !face.Bones.Zip(baseHead.Bones).All(p => p.First.Name.Equals(p.Second.Name, StringComparison.OrdinalIgnoreCase) && p.First.Parent == p.Second.Parent))
            throw new InvalidDataException("the face's skeleton isn't its base head's bones");
        var (vertexCount, vertexSize, vertexStart) = Vertices(cmesh, gmesh);
        var shift = face.Bones.Zip(baseHead.Bones, (f, b) => b.Position - f.Position).ToArray();
        var moved = (byte[])gmesh.Clone();
        largest = 0;
        for (int i = 0; i < vertexCount; i++)
        {
            int v = vertexStart + i * vertexSize;
            var move = Vector3.Zero;
            int total = 0;
            for (int k = 0; k < 4; k++)
            {
                int weight = moved[v + 20 + k], bone = moved[v + 24 + k];
                if (weight == 0) continue;
                if (bone >= shift.Length) throw new InvalidDataException($"vertex {i} is on bone {bone} of {shift.Length}");
                move += shift[bone] * weight;
                total += weight;
            }
            if (total == 0) continue;
            move /= total;
            Put(moved, v, F(moved, v) + move.X);
            Put(moved, v + 4, F(moved, v + 4) + move.Y);
            Put(moved, v + 8, F(moved, v + 8) + move.Z);
            largest = Math.Max(largest, move.Length());
        }
        return moved;
    }

    /// <summary>Where a character mesh's vertices are in its data file: how many, their size, where they start.</summary>
    static (int Count, int Size, int Start) Vertices(byte[] cmesh, byte[] gmesh)
    {
        int block = BitConverter.ToInt32(cmesh, 0x08);
        int count = BitConverter.ToInt32(cmesh, block + 0x18), size = cmesh[block + 0x1C], format = cmesh[block + 0x1D], start = BitConverter.ToInt32(cmesh, block + 0x20);
        if (format != CharacterFormat || size < 28) throw new InvalidDataException($"vertex format {format} ({size} bytes): not a character's");
        if (start + count * size > gmesh.Length) throw new InvalidDataException("the vertices run past the mesh's data file");
        return (count, size, start);
    }

    static float F(byte[] b, int at) => BitConverter.ToSingle(b, at);
    static void Put(byte[] b, int at, float value) => BitConverter.TryWriteBytes(b.AsSpan(at), value);
    static void Bytes(byte[] b, int at, Vector3 direction)
    {
        b[at] = Byte(direction.X);
        b[at + 1] = Byte(direction.Y);
        b[at + 2] = Byte(direction.Z);
    }
    static byte Byte(float x) => (byte)Math.Clamp(MathF.Round((x + 1) * 127.5f), 0, 255);
}
