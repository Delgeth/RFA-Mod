using System.Numerics;
using RfaFormats.Meshes;

namespace RfaFormats.Guerrilla;

/// <summary>
/// A zone's far-away ground: the low-detail meshes at the end of its terrain file, which the game
/// draws where the detailed sub-zones aren't loaded, made from the terrain file's own height field.
///
/// The terrain file keeps nine version-9 mesh blocks, one a sub-zone, 0xB0 bytes each (see
/// <see cref="Block"/>); their buffers are in the .gterrain_pc, block after block: the block's
/// checksum, its 16-bit indices (a triangle strip) from the next multiple of 16, its vertices from the
/// next, then the checksum again. A vertex (format 25, 8 bytes): x and z as (metres from the zone's
/// corner × 128) − 32,704, the height × 64, then the normal's x and z as bytes ((n + 1) × 127.5, the z
/// byte with its top bit flipped), as ir_terrain_height_mesh_lowlod_t reads them. The checksum ties a
/// block to its buffer (any value, as long as both carry it): ours is a hash of the buffer.
///
/// Right before the nine blocks are nine 48-byte records, one a block, which place them: 3 zeros, the
/// block's box again (from the zone's corner), then the zone's corner in the world (x, 0, z) at +0x24
/// (world_08_02's 0, 0, 1275).
///
/// The world builder takes Guerrilla's own (see <see cref="FromGuerrilla"/>); <see cref="Build"/> makes
/// them from the height field where a zone's can't be taken.
///
/// Each mesh <see cref="Build"/> makes is a regular grid over its sub-zone's third of the zone both ways (edges shared with
/// the next, so no cracks), 40 cells a side, normals from the height field. It lies under the
/// detailed ground, as the game's own do (world_08_02's are 0.5 m under it on flat ground, never
/// above, and up to 22 m under on steep ground): the game draws a sub-zone's far mesh near the
/// camera too whenever that sub-zone's detailed ground hasn't marked it as covered, which a
/// sub-zone on its zone's lowest-z edge never does for its own zone (the game finds a sub-zone's
/// zone from its origin, and the edge belongs to the next zone's row). So each vertex starts
/// <see cref="Sink"/> under the ground, and wherever a cell's two triangles (as its strip makes them)
/// pass less than that over a height field sample inside the cell, the cell's corners go down by
/// the difference: peaks keep their height, hollows the mesh would bridge go under.
/// </summary>
public static class FarGround
{
    const int Cells = 40;

    /// <summary>How far under the lowest ground around it a far vertex lies, in metres (the game's own: 0.5).</summary>
    const float Sink = 0.5f;

    /// <summary>
    /// A zone's far-away meshes from its ground: the nine records and blocks for its terrain file
    /// (<see cref="TerrainFile.FarRecords"/>, <see cref="TerrainFile.FarBlocks"/>) and its .gterrain_pc.
    /// <paramref name="heights"/> are the height field's (<paramref name="samples"/> across, <paramref name="step"/>
    /// metres apart, laid out as <see cref="TerrainHeightField.Heights"/>); <paramref name="corner"/> is the
    /// zone's corner in the world (x, z).
    /// </summary>
    public static (byte[] Records, List<byte[]> Blocks, byte[] Data) Build(float[] heights, int samples, float step, Vector2 corner, float zoneWidth)
    {
        float Height(float x, float z)
        {
            float fx = Math.Clamp(x / step, 0, samples - 1.001f), fz = Math.Clamp(z / step, 0, samples - 1.001f);
            int x0 = (int)fx, z0 = (int)fz;
            float tx = fx - x0, tz = fz - z0;
            float H(int xi, int zi) => heights[zi * samples + xi];
            return (H(x0, z0) * (1 - tx) + H(x0 + 1, z0) * tx) * (1 - tz) + (H(x0, z0 + 1) * (1 - tx) + H(x0 + 1, z0 + 1) * tx) * tz;
        }
        // A grid of far heights (i across x, j across z, from (x0, z0), cells `step` wide), each Sink
        // under the ground, then each cell's corners lowered until neither of its triangles passes
        // less than Sink over a sample inside it. Lowering a corner only lowers the cells around it, so
        // one pass leaves every cell under the ground.
        float[,] FarHeights(float x0, float z0, float cell)
        {
            var far = new float[Cells + 1, Cells + 1];
            for (int j = 0; j <= Cells; j++)
                for (int i = 0; i <= Cells; i++) far[i, j] = Height(x0 + i * cell, z0 + j * cell) - Sink;
            for (int j = 0; j < Cells; j++)
                for (int i = 0; i < Cells; i++)
                {
                    float cx = x0 + i * cell, cz = z0 + j * cell, over = 0;
                    for (int zi = Math.Max(0, (int)MathF.Ceiling(cz / step)); zi * step <= cz + cell && zi < samples; zi++)
                        for (int xi = Math.Max(0, (int)MathF.Ceiling(cx / step)); xi * step <= cx + cell && xi < samples; xi++)
                        {
                            float u = (xi * step - cx) / cell, v = (zi * step - cz) / cell;
                            // The strip's triangles: (i, j), (i, j + 1), (i + 1, j) and (i, j + 1), (i + 1, j), (i + 1, j + 1).
                            float surface = u + v <= 1
                                ? far[i, j] + u * (far[i + 1, j] - far[i, j]) + v * (far[i, j + 1] - far[i, j])
                                : far[i + 1, j + 1] + (1 - u) * (far[i, j + 1] - far[i + 1, j + 1]) + (1 - v) * (far[i + 1, j] - far[i + 1, j + 1]);
                            over = MathF.Max(over, surface - (heights[zi * samples + xi] - Sink));
                        }
                    if (over <= 0) continue;
                    far[i, j] -= over; far[i + 1, j] -= over; far[i, j + 1] -= over; far[i + 1, j + 1] -= over;
                }
            return far;
        }

        var data = new MemoryStream();
        var w = new BinaryWriter(data);
        var records = new byte[9 * 48];
        var blocks = new List<byte[]>();
        float third = zoneWidth / 3;
        for (int n = 0; n < 9; n++)
        {
            float x0 = n % 3 * third, z0 = n / 3 * third, cell = third / Cells;
            float[,] far = FarHeights(x0, z0, cell);
            var vertices = new List<(Vector3 P, Vector3 N)>();
            for (int j = 0; j <= Cells; j++)
                for (int i = 0; i <= Cells; i++)
                {
                    float x = x0 + i * cell, z = z0 + j * cell;
                    var normal = Vector3.Normalize(new Vector3(Height(x - 1, z) - Height(x + 1, z), 2, Height(x, z - 1) - Height(x, z + 1)));
                    vertices.Add((new Vector3(x, far[i, j], z), normal));
                }
            int V(int i, int j) => j * (Cells + 1) + i;
            // A strip per row of cells, (i, j) and (i, j + 1) in turn: as the game reads strips (a
            // triangle at an even place a, b, c; at an odd place b, a, c) each cell's two triangles,
            // facing up. Rows are joined by repeating the row's last index and the next row's first,
            // which keeps the next row on an even place.
            var strip = new List<int>();
            for (int j = 0; j < Cells; j++)
            {
                if (j > 0) { strip.Add(strip[^1]); strip.Add(V(0, j)); }
                for (int i = 0; i <= Cells; i++) { strip.Add(V(i, j)); strip.Add(V(i, j + 1)); }
            }

            // The buffer, first with a placeholder for the checksum (a hash of the rest, written after).
            int start = (int)data.Length;
            w.Write(0u);
            while (data.Length % 16 != 0) w.Write((byte)0);
            int indexOffset = (int)data.Length - start;
            foreach (int i in strip) w.Write((ushort)i);
            while (data.Length % 16 != 0) w.Write((byte)0);
            int vertexOffset = (int)data.Length - start;
            foreach (var (p, nrm) in vertices)
            {
                w.Write((short)Math.Clamp(MathF.Round(p.X * 128) - 32704, short.MinValue, short.MaxValue));
                w.Write((short)Math.Clamp(MathF.Round(p.Y * 64), short.MinValue, short.MaxValue));
                w.Write((short)Math.Clamp(MathF.Round(p.Z * 128) - 32704, short.MinValue, short.MaxValue));
                w.Write((byte)Math.Clamp(MathF.Round((nrm.X + 1) * 127.5f), 0, 255));
                w.Write((byte)((byte)Math.Clamp(MathF.Round((nrm.Z + 1) * 127.5f), 0, 255) ^ 0x80));
            }
            uint checksum = 2166136261;
            foreach (byte x in data.GetBuffer().AsSpan(start + 16, (int)data.Length - start - 16)) checksum = (checksum ^ x) * 16777619;
            if (checksum == 0) checksum = 1;
            w.Write(checksum);
            data.Position = start;
            w.Write(checksum);
            data.Position = data.Length;
            int gSize = (int)data.Length - start;

            Vector3 min = new(vertices.Min(v => v.P.X), vertices.Min(v => v.P.Y), vertices.Min(v => v.P.Z));
            Vector3 max = new(vertices.Max(v => v.P.X), vertices.Max(v => v.P.Y), vertices.Max(v => v.P.Z));
            blocks.Add(Block(checksum, gSize, vertices.Count, vertexOffset, strip.Count, indexOffset, min, max));
            float[] placing = [min.X, min.Y, min.Z, max.X, max.Y, max.Z, corner.X, 0, corner.Y];
            for (int k = 0; k < placing.Length; k++) BitConverter.TryWriteBytes(records.AsSpan(n * 48 + 0x0C + k * 4), placing[k]);
        }
        return (records, blocks, data.ToArray());
    }

    /// <summary>Our far meshes' vertex format (Guerrilla's 15 is the same 8 bytes: see the top).</summary>
    const byte FarFormat = 25;

    /// <summary>How far under the ground (at its samples) a far mesh taken from Guerrilla must stay (m); lower, it is lowered (see <see cref="FromGuerrilla"/>).</summary>
    const float GuerrillaSinkKept = 0.05f;

    /// <summary>
    /// A zone's far meshes from Guerrilla's own, for our terrain file and .gterrain_pc (as
    /// <see cref="Build"/>'s): Guerrilla's terrain file (<paramref name="terrain"/>, its .cterrain_pc)
    /// keeps them as Armageddon's does, nine 48-byte records then nine mesh blocks, but of version 6
    /// with vertex format 15 (the same 8 bytes as Armageddon's 25, the normal's z byte's top bit flipped
    /// as well), near the file's end; their buffers are its .gterrain_pc's first bytes (<paramref name="data"/>),
    /// block after block, each from where the last ends, its indices and vertices at multiples of 16 in
    /// the file. Each block is made version 9 (<see cref="MeshBlockConversion"/>) with format 25, the
    /// buffers kept where they are (so their alignment holds), the records as Guerrilla's. Guerrilla's
    /// lie half a metre under its ground (more on steep ground) on an adaptive mesh, where ours from the
    /// height field are a regular grid sunk deeper. Where one would come within
    /// <see cref="GuerrillaSinkKept"/> of our ground (our <paramref name="heights"/>, the height field's,
    /// <paramref name="samples"/> across, <paramref name="step"/> apart) at a sample inside one of its
    /// triangles, that triangle's corners go down by the difference (the game draws some far meshes
    /// near the camera too: see the top). Returns those and how many vertices were lowered.
    /// </summary>
    public static (byte[] Records, List<byte[]> Blocks, byte[] Data, int Lowered) FromGuerrilla(byte[] terrain, byte[] data, float[] heights, int samples, float step, Vector2 corner)
    {
        int I(byte[] d, int at) => BitConverter.ToInt32(d, at);
        var (records, first, _) = GuerrillaTerrain.FarMeshes(terrain);
        var blocksAt = new List<int>();
        for (int at = first, k = 0; k < 9; k++, at += I(terrain, at + 8)) blocksAt.Add(at);
        byte[] placing = terrain[records..first];
        for (int k = 0; k < 9; k++)
        {
            float cx = BitConverter.ToSingle(placing, k * 48 + 0x24), cz = BitConverter.ToSingle(placing, k * 48 + 0x2C);
            if (MathF.Abs(cx - corner.X) > 0.01f || MathF.Abs(cz - corner.Y) > 0.01f)
                throw new InvalidDataException($"Guerrilla's far mesh record {k} places it at ({cx}, {cz}), not the zone's corner ({corner.X}, {corner.Y})");
        }

        float Ground(int xi, int zi) => heights[Math.Clamp(zi, 0, samples - 1) * samples + Math.Clamp(xi, 0, samples - 1)];
        var blocks = new List<byte[]>();
        int g = 0, lowered = 0;
        var buffers = new MemoryStream();
        foreach (int at in blocksAt)
        {
            uint checksum = BitConverter.ToUInt32(terrain, at + 4);
            int size = I(terrain, at + 0x0C), vertices = I(terrain, at + 0x18), vertexOffset = I(terrain, at + 0x20);
            int indices = I(terrain, at + 0x24), indexOffset = I(terrain, at + 0x28);
            if (g + size > data.Length || BitConverter.ToUInt32(data, g) != checksum || BitConverter.ToUInt32(data, g + size - 4) != checksum ||
                vertexOffset + vertices * 8 > size || indexOffset + indices * 2 > size || terrain[at + 0x2C] != 2 || terrain[at + 0x2D] != 0)
                throw new InvalidDataException($"Guerrilla's far mesh block at 0x{at:X} doesn't match its buffer at {g}");
            byte[] buffer = data[g..(g + size)];
            short Y(int v) => BitConverter.ToInt16(buffer, vertexOffset + v * 8 + 2);
            Vector2 Place(int v) => new((BitConverter.ToInt16(buffer, vertexOffset + v * 8) + 32704) / 128f, (BitConverter.ToInt16(buffer, vertexOffset + v * 8 + 4) + 32704) / 128f);
            // Each triangle of the strip (repeats skip), and how far each vertex must go down.
            var down = new float[vertices];
            for (int i = 0; i + 2 < indices; i++)
            {
                int a = BitConverter.ToUInt16(buffer, indexOffset + i * 2), b = BitConverter.ToUInt16(buffer, indexOffset + i * 2 + 2), c = BitConverter.ToUInt16(buffer, indexOffset + i * 2 + 4);
                if (a == b || b == c || a == c || a >= vertices || b >= vertices || c >= vertices) continue;
                Vector2 pa = Place(a), pb = Place(b), pc = Place(c);
                float ya = Y(a) / 64f, yb = Y(b) / 64f, yc = Y(c) / 64f;
                float area = (pb.X - pa.X) * (pc.Y - pa.Y) - (pc.X - pa.X) * (pb.Y - pa.Y);
                if (MathF.Abs(area) < 1e-9f) continue;
                float over = 0;
                int x1 = (int)MathF.Ceiling(MathF.Min(pa.X, MathF.Min(pb.X, pc.X)) / step), x2 = (int)MathF.Floor(MathF.Max(pa.X, MathF.Max(pb.X, pc.X)) / step);
                int z1 = (int)MathF.Ceiling(MathF.Min(pa.Y, MathF.Min(pb.Y, pc.Y)) / step), z2 = (int)MathF.Floor(MathF.Max(pa.Y, MathF.Max(pb.Y, pc.Y)) / step);
                for (int zi = Math.Max(z1, 0); zi <= Math.Min(z2, samples - 1); zi++)
                    for (int xi = Math.Max(x1, 0); xi <= Math.Min(x2, samples - 1); xi++)
                    {
                        float px = xi * step, pz = zi * step;
                        float wa = ((pb.X - px) * (pc.Y - pz) - (pc.X - px) * (pb.Y - pz)) / area;
                        float wb = ((pc.X - px) * (pa.Y - pz) - (pa.X - px) * (pc.Y - pz)) / area;
                        float wc = 1 - wa - wb;
                        if (wa < -1e-4f || wb < -1e-4f || wc < -1e-4f) continue;
                        over = MathF.Max(over, wa * ya + wb * yb + wc * yc - (Ground(xi, zi) - GuerrillaSinkKept));
                    }
                if (over <= 0) continue;
                down[a] = MathF.Max(down[a], over);
                down[b] = MathF.Max(down[b], over);
                down[c] = MathF.Max(down[c], over);
            }
            float lowest = float.MaxValue;
            for (int v = 0; v < vertices; v++)
            {
                if (down[v] > 0)
                {
                    short y = (short)Math.Clamp(MathF.Floor(Y(v) - down[v] * 64), short.MinValue, short.MaxValue);
                    BitConverter.TryWriteBytes(buffer.AsSpan(vertexOffset + v * 8 + 2), y);
                    lowered++;
                }
                lowest = MathF.Min(lowest, Y(v) / 64f);
            }
            buffers.Write(buffer);

            byte[] block = MeshBlockConversion.FromVersion6(terrain, at);
            block[0x21] = FarFormat;
            // The boxes (the record's and the submesh's) down to the lowest vertex.
            int k = blocks.Count;
            float recordLow = BitConverter.ToSingle(placing, k * 48 + 0x10);
            if (lowest < recordLow) BitConverter.TryWriteBytes(placing.AsSpan(k * 48 + 0x10), lowest);
            if (lowest < BitConverter.ToSingle(block, 0x74)) BitConverter.TryWriteBytes(block.AsSpan(0x74), lowest);
            blocks.Add(block);
            g += size;
        }
        return (placing, blocks, buffers.ToArray(), lowered);
    }

    /// <summary>
    /// A far mesh's version-9 mesh block (0xB0 bytes), as the game's: version 9, the checksum, the
    /// block's size, its buffer's size; 1 level, 1 submesh at 0x60, the vertex count; vertices of 8
    /// bytes in format 25 (no texture coordinates); six floats 1 1 1 0 0 0; the vertex offset, index
    /// count and index offset in the buffer; 2-byte indices as one strip (topology 0), 1 render block;
    /// the submesh (1, its pivot at the corner, its box; two run-time words); the render block (a
    /// run-time material word, from index 0, the index count, 0, the index count - 1);
    /// 8 zero bytes and the checksum again.
    /// </summary>
    public static byte[] Block(uint checksum, int bufferSize, int vertexCount, int vertexOffset, int indexCount, int indexOffset, Vector3 min, Vector3 max)
    {
        var b = new byte[0xB0];
        void I(int at, uint v) => BitConverter.TryWriteBytes(b.AsSpan(at), v);
        void F(int at, float v) => BitConverter.TryWriteBytes(b.AsSpan(at), v);
        I(0x00, 9); I(0x04, checksum); I(0x08, 0xB0); I(0x0C, (uint)bufferSize);
        I(0x10, 1); I(0x14, 1); I(0x18, 0x60); I(0x1C, (uint)vertexCount);
        b[0x20] = 8; b[0x21] = 25;
        F(0x24, 1); F(0x28, 1); F(0x2C, 1);
        I(0x3C, (uint)vertexOffset); I(0x40, (uint)indexCount); I(0x44, (uint)indexOffset);
        b[0x48] = 2; b[0x4A] = 1;
        I(0x60, 1);
        F(0x70, min.X); F(0x74, min.Y); F(0x78, min.Z); F(0x7C, max.X); F(0x80, max.Y); F(0x84, max.Z);
        I(0x98, (uint)indexCount); I(0xA0, (uint)(indexCount - 1));
        I(0xAC, checksum);
        return b;
    }
}
