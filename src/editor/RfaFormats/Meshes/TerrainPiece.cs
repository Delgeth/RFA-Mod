using System.Numerics;
using System.Text;

namespace RfaFormats.Meshes;

/// <summary>What a terrain piece is shaped like (see <see cref="TerrainPiece"/>).</summary>
/// <param name="Kind">The landform: Hill, Mountain, Mesa, Ridge, Dunes, Crater, Rolling, Slab or Ramp.</param>
/// <param name="Size">How far it reaches across, both ways (metres).</param>
/// <param name="Height">How high its landform rises (metres).</param>
/// <param name="Feature">How big its bumps, ridges and dunes are (metres).</param>
/// <param name="Seed">The same seed makes the same piece again.</param>
/// <param name="Resolution">How many squares across (even; 8 to 120).</param>
/// <param name="Skirt">How far its sides reach down below its lowest point (metres; 0 none).</param>
/// <param name="Tile">How many metres its texture covers before it repeats.</param>
public sealed record TerrainPieceShape(string Kind, float Size, float Height, float Feature, int Seed, int Resolution, float Skirt, float Tile)
{
    public static readonly string[] Kinds = ["Hill", "Mountain", "Mesa", "Butte", "Badlands", "Ridge", "Dunes", "Crater", "Rolling", "Slab", "Ramp"];

    /// <summary>As one line of text (to keep it, and to name the piece by).</summary>
    public string Text => string.Join(";", Kind, Num(Size), Num(Height), Num(Feature), Seed, Resolution, Num(Skirt), Num(Tile));

    static string Num(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    public static TerrainPieceShape? Parse(string text)
    {
        string[] p = text.Split(';');
        if (p.Length != 8) return null;
        static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        try { return new TerrainPieceShape(p[0], F(p[1]), F(p[2]), F(p[3]), int.Parse(p[4]), int.Parse(p[5]), F(p[6]), F(p[7])); }
        catch (FormatException) { return null; }
    }
}

/// <summary>
/// A terrain piece: a stitch piece (level geometry: .cstch_pc and .gstch_pc) made from nothing but a
/// landform, so it can be placed, moved, turned and deleted like any other, and drawn and stood on
/// in the game. It is written over another stitch piece of the game's (its template: its look comes
/// from that piece's material and textures), keeping every byte of the template but:
///   the mesh block (at 0x200): the g-file's size, the vertex count and offset, the index count and
///     offset; each submesh's box; each render block's first index, count and vertex range. The
///     block keeps its size: the template must have one or two submeshes (the second its far-away
///     copy), one render block each, 16-bit strip indices
///   the g-file: the template's own start (its checksum) and end, the new indices and vertices in
///     the template's vertex format (position, normal, tangent, texture coordinates)
///   the collision section: one triangle mesh under a tree (<see cref="PieceCollision.Section(IReadOnlyList{Vector3}, IReadOnlyList{int})"/>),
///     and what follows it moved to the next multiple of 64 after it
///   the template's box, wherever its corners appear outside those (the header's and its objects')
///   its name, where the header has the template's (0x30 and 0xB0), and the g-file's size (0x1BC).
/// The piece is a height field Size × Size metres around its own origin, with sides (a skirt) down
/// below its lowest point so it shows no gap where it meets the ground. The far-away copy is the same
/// at half the detail (every other vertex).
/// </summary>
public static class TerrainPiece
{
    const int MeshAt = 0x200;

    static int I(byte[] d, int at) => BitConverter.ToInt32(d, at);
    static float F(byte[] d, int at) => BitConverter.ToSingle(d, at);

    /// <summary>Why a stitch piece can't be a terrain piece's template; null if it can.</summary>
    public static string? Unsuitable(byte[] c, byte[] g)
    {
        if (c.Length < MeshAt + 0x60 || I(c, 0x10) != MeshAt || I(c, MeshAt) != 9) return "its mesh block isn't a version-9 one at 0x200";
        int submeshes = I(c, MeshAt + 0x14), table = MeshAt + I(c, MeshAt + 0x18), blocks = BitConverter.ToUInt16(c, MeshAt + 0x4A);
        if (submeshes is not (1 or 2) || blocks != submeshes) return $"{submeshes} submeshes and {blocks} render blocks, not one or two of each";
        for (int s = 0; s < submeshes; s++) if (I(c, table + s * 48) != 1) return "a submesh with more than one render block";
        if (c[MeshAt + 0x48] != 2 || c[MeshAt + 0x49] != 0) return "its indices aren't 16-bit strips";
        int size = c[MeshAt + 0x20], uvs = c[MeshAt + 0x22];
        if (size < 24 || size - 4 * uvs < 16 || uvs < 1) return $"{size}-byte vertices with {uvs} texture coordinate sets";
        int vertices = I(c, MeshAt + 0x3C), indices = I(c, MeshAt + 0x44), count = I(c, MeshAt + 0x1C);
        if (indices >= vertices || vertices + (long)count * size > g.Length) return "its g-file isn't laid out as expected (indices, then vertices)";
        if (!PieceCollision.IsMesh(c)) return "its collision isn't one triangle mesh";
        return null;
    }

    // ---- The landform ---------------------------------------------------------------------------

    static float Noise(float x, float z, int seed)
    {
        int x0 = (int)MathF.Floor(x), z0 = (int)MathF.Floor(z);
        float tx = x - x0, tz = z - z0;
        tx = tx * tx * (3 - 2 * tx);
        tz = tz * tz * (3 - 2 * tz);
        float Corner(int cx, int cz)
        {
            uint h = unchecked((uint)(cx * 374761393 + cz * 668265263 + seed * 2147483647));
            h = unchecked((h ^ (h >> 13)) * 1274126177);
            return (h ^ (h >> 16)) / (float)uint.MaxValue;
        }
        float a = Corner(x0, z0), b = Corner(x0 + 1, z0), c = Corner(x0, z0 + 1), d = Corner(x0 + 1, z0 + 1);
        return (a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + d * tx) * tz;
    }

    static float Fbm(float x, float z, float scale, int octaves, int seed)
    {
        float total = 0, weight = 1, all = 0;
        for (int k = 0; k < octaves; k++, scale *= 0.5f, weight *= 0.5f)
        {
            total += Noise(x / scale, z / scale, seed + k) * weight;
            all += weight;
        }
        return total / all;
    }

    static float SmoothStep(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>The landform's height at a place (x, z from the piece's middle).</summary>
    public static float HeightAt(TerrainPieceShape s, float x, float z)
    {
        float half = s.Size / 2, H = s.Height, f = MathF.Max(1, s.Feature);
        float r = MathF.Sqrt(x * x + z * z) / half;                      // 0 in the middle, 1 at the sides' middles
        float edge = 1 - SmoothStep(0.55f, 1.0f, r);                      // fades out toward the edge
        int seed = s.Seed * 31;
        float detail = (Fbm(x, z, f * 0.5f, 3, seed + 50) - 0.5f) * 0.15f;
        switch (s.Kind)
        {
            case "Mountain":
            {
                float ridged = 0, weight = 1, all = 0, scale = f;
                for (int k = 0; k < 5; k++, scale *= 0.5f, weight *= 0.5f)
                {
                    float n = 1 - MathF.Abs(Noise(x / scale, z / scale, seed + 100 + k) * 2 - 1);
                    ridged += n * n * weight;
                    all += weight;
                }
                float cone = MathF.Pow(1 - SmoothStep(0, 1, r), 1.4f);
                return H * cone * (0.55f + 0.45f * ridged / all);
            }
            case "Mesa":
            {
                float rim = r + (Fbm(x, z, f, 3, seed + 11) - 0.5f) * 0.35f;
                return H * (1 - SmoothStep(0.6f, 0.8f, rim)) * (1 + detail * 0.3f);
            }
            case "Butte":
            {
                // A mesa whose sides step down in ledges (Guerrilla's Mars).
                float rim = r + (Fbm(x, z, f, 3, seed + 12) - 0.5f) * 0.3f;
                float stepped = (1 - SmoothStep(0.35f, 0.85f, rim)) * 4, ledge = MathF.Floor(stepped);
                return H * (ledge + SmoothStep(0.6f, 1, stepped - ledge)) / 4;
            }
            case "Badlands":
            {
                float rise = SmoothStep(0.42f, 0.62f, Fbm(x, z, f, 4, seed + 15)) * edge;
                float stepped = rise * 5, ledge = MathF.Floor(stepped);
                return H * (ledge + SmoothStep(0.6f, 1, stepped - ledge)) / 5;
            }
            case "Ridge":
            {
                float across = MathF.Abs(z + (Fbm(x, 0, f * 2, 2, seed + 7) - 0.5f) * half * 0.5f) / half;
                float along = 1 - SmoothStep(0.6f, 1, MathF.Abs(x) / half);
                return H * MathF.Pow(1 - SmoothStep(0, 0.8f, across), 1.3f) * along * (1 + detail);
            }
            case "Dunes":
            {
                float angle = (s.Seed % 360) * MathF.PI / 180;
                float u = (x * MathF.Cos(angle) + z * MathF.Sin(angle)) / f + (Fbm(x, z, f * 2, 2, seed + 13) - 0.5f) * 3;
                float wave = u - MathF.Floor(u);
                float profile = wave < 0.75f ? wave / 0.75f : (1 - wave) / 0.25f;
                return H * (0.5f - 0.5f * MathF.Cos(MathF.PI * profile)) * edge;
            }
            case "Crater":
            {
                float rr = r / 0.7f;
                float bowl = rr < 1 ? -H * 0.7f * (1 - rr * rr) : 0;
                float rimH = H * 0.5f * MathF.Exp(-MathF.Pow((rr - 1) / 0.2f, 2));
                return (bowl + rimH) * edge + H * detail * 0.3f;
            }
            case "Rolling":
                return H * Fbm(x, z, f, 4, seed) * edge;
            case "Slab":
                return H * (1 + detail * 0.2f);
            case "Ramp":
                return H * Math.Clamp((x + half) / s.Size, 0, 1);
            default:  // Hill
                return H * 0.5f * (1 + MathF.Cos(MathF.PI * Math.Min(1, r))) * (1 + detail);
        }
    }

    // ---- The files ------------------------------------------------------------------------------

    /// <summary>
    /// The piece's two files, written over the template's (see the top). <paramref name="templateName"/>
    /// and <paramref name="name"/>: the pieces' names without ".cstch_pc".
    /// </summary>
    public static (byte[] C, byte[] G) Write(string templateName, byte[] templateC, byte[] templateG, string name, TerrainPieceShape shape)
    {
        if (Unsuitable(templateC, templateG) is string why) throw new NotSupportedException($"{templateName} can't be a template: {why}");
        int n = Math.Clamp(shape.Resolution / 2 * 2, 8, 120);
        float size = MathF.Max(1, shape.Size), step = size / n, half = size / 2;
        int row = n + 1;

        // The height field's vertices (x, z from the middle), and their normals.
        var positions = new List<Vector3>(row * row + 8 * n);
        var normals = new List<Vector3>(row * row + 8 * n);
        var heights = new float[row * row];
        for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
                heights[z * row + x] = HeightAt(shape, -half + x * step, -half + z * step);
        float H(int x, int z) => heights[Math.Clamp(z, 0, n) * row + Math.Clamp(x, 0, n)];
        for (int z = 0; z <= n; z++)
            for (int x = 0; x <= n; x++)
            {
                positions.Add(new Vector3(-half + x * step, H(x, z), -half + z * step));
                normals.Add(Vector3.Normalize(new Vector3(-(H(x + 1, z) - H(x - 1, z)) / (2 * step), 1, -(H(x, z + 1) - H(x, z - 1)) / (2 * step))));
            }
        float lowest = heights.Min();

        // The strips: the field a row of squares at a time (rows joined by repeated indices); a = this
        // row's vertex, b = the next row's (higher z): triangles (a0, b0, a1), (a1, b0, b1) face up.
        List<int> Field(int every)
        {
            var strip = new List<int>();
            for (int z = 0; z < n; z += every)
            {
                if (strip.Count > 0) { strip.Add(strip[^1]); strip.Add(z * row); }
                for (int x = 0; x <= n; x += every)
                {
                    strip.Add(z * row + x);
                    strip.Add((z + every) * row + x);
                }
            }
            return strip;
        }
        List<int> near = Field(1), far = Field(2);

        // The skirt: round the edge (each edge vertex, and one below it), facing out.
        if (shape.Skirt > 0)
        {
            var ring = new List<(int X, int Z)>();
            for (int x = 0; x < n; x++) ring.Add((x, 0));
            for (int z = 0; z < n; z++) ring.Add((n, z));
            for (int x = n; x > 0; x--) ring.Add((x, n));
            for (int z = n; z > 0; z--) ring.Add((0, z));
            ring.Add(ring[0]);
            // Which way round faces out: the first triangle's (top0, bottom0, top1) facing, by the edge's outward side (−z).
            Vector3 t0 = positions[ring[0].Z * row + ring[0].X], t1 = positions[ring[1].Z * row + ring[1].X];
            Vector3 facing = Vector3.Cross(new Vector3(0, -1, 0), t1 - t0);
            if (facing.Z > 0) ring.Reverse();
            float bottom = lowest - shape.Skirt;
            var skirt = new List<int>();
            for (int i = 0; i < ring.Count; i++)
            {
                var (x, z) = ring[i];
                Vector3 top = positions[z * row + x];
                Vector3 outward = x == 0 && z != 0 && z != n ? -Vector3.UnitX : x == n && z != 0 && z != n ? Vector3.UnitX
                                : z == 0 ? -Vector3.UnitZ : z == n ? Vector3.UnitZ : -Vector3.UnitX;
                int first = positions.Count;
                positions.Add(top);
                normals.Add(outward);
                positions.Add(new Vector3(top.X, bottom, top.Z));
                normals.Add(outward);
                skirt.Add(first);
                skirt.Add(first + 1);
            }
            foreach (List<int> strip in new[] { near, far })
            {
                strip.Add(strip[^1]);
                strip.Add(skirt[0]);
                strip.AddRange(skirt);
            }
        }
        if (positions.Count > 65535) throw new ArgumentException("too many vertices for one piece");

        // The template: its vertex format and where its one submesh goes (the vertices are written in its space).
        ModelFile template = ModelFile.Read(templateName + ".cstch_pc", templateC, templateG);
        IReadOnlyList<PlacedSubmesh> intact = template.Intact();
        Matrix4x4 placed = intact.Count > 0 ? intact[0].Transform : Matrix4x4.Identity;
        if (!Matrix4x4.Invert(placed, out Matrix4x4 toSubmesh)) toSubmesh = Matrix4x4.Identity;
        int vertexSize = templateC[MeshAt + 0x20], uvSets = templateC[MeshAt + 0x22], uvAt = vertexSize - 4 * uvSets;
        int templateVertices = I(templateC, MeshAt + 0x3C), templateIndices = I(templateC, MeshAt + 0x44), templateCount = I(templateC, MeshAt + 0x1C);
        byte[] like = templateG.AsSpan(templateVertices, vertexSize).ToArray();

        static byte Unit(float v) => (byte)Math.Clamp(MathF.Round((v + 1) * 127.5f), 0, 255);
        static short Coordinate(float v) => (short)Math.Clamp(MathF.Round(v * 1024), short.MinValue, short.MaxValue);
        var vertexBytes = new byte[positions.Count * vertexSize];
        Vector3 boxMin = new(float.MaxValue), boxMax = new(float.MinValue);
        float tile = MathF.Max(0.5f, shape.Tile);
        for (int v = 0; v < positions.Count; v++)
        {
            Span<byte> o = vertexBytes.AsSpan(v * vertexSize, vertexSize);
            like.CopyTo(o);
            Vector3 p = Vector3.Transform(positions[v], toSubmesh);
            Vector3 nrm = Vector3.Normalize(Vector3.TransformNormal(normals[v], toSubmesh));
            boxMin = Vector3.Min(boxMin, p);
            boxMax = Vector3.Max(boxMax, p);
            BitConverter.TryWriteBytes(o[0..], p.X); BitConverter.TryWriteBytes(o[4..], p.Y); BitConverter.TryWriteBytes(o[8..], p.Z);
            o[12] = Unit(nrm.X); o[13] = Unit(nrm.Y); o[14] = Unit(nrm.Z);
            if (uvAt >= 20)
            {
                // The tangent: along x, square to the normal (a side of the skirt along z: along z).
                Vector3 along = MathF.Abs(nrm.X) > 0.9f ? Vector3.UnitZ : Vector3.UnitX;
                Vector3 tangent = Vector3.Normalize(along - nrm * Vector3.Dot(along, nrm));
                o[16] = Unit(tangent.X); o[17] = Unit(tangent.Y); o[18] = Unit(tangent.Z);
            }
            // Its texture across the top from above; the skirt's sides from the side.
            Vector3 w = positions[v];
            bool side = v >= row * row;
            float u = side && MathF.Abs(normals[v].X) > 0.5f ? w.Z : w.X;
            float vv = side ? -w.Y : w.Z;
            BitConverter.TryWriteBytes(o[uvAt..], Coordinate(u / tile));
            BitConverter.TryWriteBytes(o[(uvAt + 2)..], Coordinate(vv / tile));
            for (int set = 1; set < uvSets; set++)
            {
                BitConverter.TryWriteBytes(o[(uvAt + set * 4)..], Coordinate((w.X + half) / size));
                BitConverter.TryWriteBytes(o[(uvAt + set * 4 + 2)..], Coordinate((w.Z + half) / size));
            }
        }

        // The g-file: the template's start, the indices (near, then far), the vertices, the template's end.
        var gOut = new List<byte>(templateG.AsSpan(0, templateIndices).ToArray());
        int submeshes = I(templateC, MeshAt + 0x14);
        List<int> all = submeshes == 2 ? near.Concat(far).ToList() : near;
        foreach (int index in all) gOut.AddRange(BitConverter.GetBytes((ushort)index));
        while (gOut.Count % 16 != 0) gOut.Add(0);
        int vertexOffset = gOut.Count;
        gOut.AddRange(vertexBytes);
        int templateEnd = (templateVertices + templateCount * vertexSize + 3) & ~3;
        while (gOut.Count % 4 != 0) gOut.Add(0);
        if (templateEnd < templateG.Length) gOut.AddRange(templateG.AsSpan(templateEnd).ToArray());
        byte[] g = [.. gOut];

        // The mesh block.
        byte[] c = (byte[])templateC.Clone();
        void PutI(int at, int value) => BitConverter.TryWriteBytes(c.AsSpan(at), value);
        void PutV(int at, Vector3 value) { BitConverter.TryWriteBytes(c.AsSpan(at), value.X); BitConverter.TryWriteBytes(c.AsSpan(at + 4), value.Y); BitConverter.TryWriteBytes(c.AsSpan(at + 8), value.Z); }
        int table = MeshAt + I(c, MeshAt + 0x18), blockTable = table + submeshes * 48;
        // The template's box (all its submeshes), to find it elsewhere in the file.
        Vector3 oldMin = new(F(c, table + 16), F(c, table + 20), F(c, table + 24)), oldMax = new(F(c, table + 28), F(c, table + 32), F(c, table + 36));
        PutI(MeshAt + 0x0C, g.Length);
        PutI(MeshAt + 0x1C, positions.Count);
        PutI(MeshAt + 0x3C, vertexOffset);
        PutI(MeshAt + 0x40, all.Count);
        PutI(MeshAt + 0x44, templateIndices);
        for (int s = 0; s < submeshes; s++)
        {
            PutV(table + s * 48 + 16, boxMin);
            PutV(table + s * 48 + 28, boxMax);
            int block = blockTable + s * 20;
            PutI(block + 4, s == 0 ? 0 : near.Count);
            PutI(block + 8, s == 0 ? near.Count : far.Count);
            PutI(block + 12, 0);
            PutI(block + 16, positions.Count - 1);
        }
        PutI(0x1BC, g.Length);
        int meshEnd = MeshAt + I(c, MeshAt + 8);

        // The collision: the near strip's triangles, where the piece is (not the submesh's space).
        int[] triangles = ModelFile.StripToTriangles([.. near], 0, near.Count);
        byte[] collision = PieceCollision.Section(positions, triangles);
        int collisionAt = I(c, 0x1C), collisionLength = I(c, 0x20);
        int restAt = (collisionAt + collisionLength + 63) & ~63;
        var cOut = new List<byte>(c.AsSpan(0, collisionAt).ToArray());
        cOut.AddRange(collision);
        while (cOut.Count % 64 != 0) cOut.Add(0);
        if (restAt < c.Length) cOut.AddRange(c.AsSpan(restAt).ToArray());
        c = [.. cOut];
        PutI(0x20, collision.Length);

        // The template's box wherever else it appears (the header's, its objects'), and its name.
        int restNow = (collisionAt + collision.Length + 63) & ~63;
        void Replace(int from, int to)
        {
            for (int at = from; at + 12 <= to; at += 4)
            {
                if (Same(c, at, oldMin)) PutV(at, boxMin);
                else if (Same(c, at, oldMax)) PutV(at, boxMax);
            }
        }
        Replace(0x20 + 4, MeshAt);
        Replace(meshEnd, collisionAt);
        Replace(restNow, c.Length);
        ReplaceName(c, 0x30, templateName, name);
        ReplaceName(c, 0xB0, templateName, name);
        return (c, g);
    }

    static bool Same(byte[] c, int at, Vector3 v) =>
        BitConverter.SingleToInt32Bits(F(c, at)) == BitConverter.SingleToInt32Bits(v.X) &&
        BitConverter.SingleToInt32Bits(F(c, at + 4)) == BitConverter.SingleToInt32Bits(v.Y) &&
        BitConverter.SingleToInt32Bits(F(c, at + 8)) == BitConverter.SingleToInt32Bits(v.Z) &&
        !(v.X == 0 && v.Y == 0 && v.Z == 0);

    /// <summary>A 128-byte name field holding the template's name (perhaps with an extension): the new name in its place.</summary>
    static void ReplaceName(byte[] c, int at, string templateName, string name)
    {
        if (at + 128 > c.Length) return;
        int end = Array.IndexOf(c, (byte)0, at, 128);
        if (end < 0) return;
        string now = Encoding.ASCII.GetString(c, at, end - at);
        if (!now.StartsWith(templateName, StringComparison.OrdinalIgnoreCase)) return;
        string made = name + now[templateName.Length..];
        if (made.Length > 127) return;
        Array.Clear(c, at, 128);
        Encoding.ASCII.GetBytes(made).CopyTo(c, at);
    }
}
