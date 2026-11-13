using System.IO;
using System.Numerics;
using RfaFormats.Meshes;

namespace RfaEditor;

/// <summary>What a stroke of the terrain sculptor does to the ground under the brush.</summary>
/// <summary>
/// What a stroke does: Raise and Lower move the ground by the brush's soft round fall-off; Smooth and
/// Flatten even it out; Hill raises a broad, gently rounded rise (a cosine bell); Peak a sharp point
/// (falling off steeply from the middle); Mountain a rugged rise (the bell broken up by ridged noise,
/// so strokes build crags and spurs); Valley carves a trough with a wide flat floor and steep sides.
/// Hill, Peak and Mountain go down instead with the opposite (Shift), Valley up (a ridge);
/// The Martian landforms of Guerrilla's world: Mesa raises a flat-topped butte with steep, ragged
/// sides; Terrace cuts the ground into ledges (the strata of its cliffs); Cliff steepens the ground
/// at the height the stroke began (above it up, below it down); Erode carves branching gullies;
/// Canyon a deep cut whose walls step down in ledges; Dunes lays wind ripples; Crater an impact
/// bowl with a raised rim; Roughen breaks the ground up into rubble. Shift gives the opposite
/// (a pit, a mound, softer slopes, ridges, a stepped butte, a dome, smoother ground).
/// PaintFoliage and EraseFoliage are the foliage painter's (see LevelEditor.Foliage.cs), not the ground's;
/// PaintGround (the ground's texture layers) and PaintRoad (a graded road, painted) are the ground
/// painter's (see LevelEditor.GroundPaint.cs).
/// </summary>
enum SculptMode { Raise, Lower, Smooth, Flatten, Hill, Peak, Mountain, Valley, Mesa, Terrace, Cliff, Erode, Canyon, Dunes, Crater, Roughen,
                 PaintFoliage, EraseFoliage, PaintGround, PaintRoad, PaintChunks, EraseChunks }

/// <summary>
/// A zone's ground, held for sculpting: its collision height field (in the terrain file,
/// &lt;zone&gt;.cterrain_pc: see <see cref="TerrainHeightField"/>) and the detailed ground the game
/// draws (its nine sub-zones, &lt;zone&gt;_N.ctmesh_pc / .gtmesh_pc: see <see cref="TerrainSubzone"/>).
///
/// The height field is what a stroke changes (its samples lie on the same grid as the ground's
/// vertices, one a grid step apart); the vertices under it then take the field's height where they
/// stand, and their normals its slope, so what the player walks on and what is drawn stay the same.
/// The vertices' places and the triangles don't change: where the game's ground is coarse (big
/// flat triangles) a stroke smaller than them shows only in the collision. Saving writes the
/// terrain file and the changed sub-zones (same sizes) wherever the game has them.
///
/// Not changed: the far-away ground (the terrain file's low-detail meshes), which still shows the
/// old shape from a distance; the zones around (a stroke stops at the zone's edge); foliage and
/// objects standing on the ground (they stay where they are).
/// </summary>
sealed class TerrainSculpt
{
    /// <summary>One sub-zone's ground: its files as read and its vertices' heights and normals now.</summary>
    public sealed class Part
    {
        public required int Number { get; init; }
        public required string HeaderName { get; init; }
        public required string DataName { get; init; }
        public required byte[] Header { get; init; }
        public required byte[] Data { get; init; }
        public required TerrainSubzone Subzone { get; init; }
        public required float[] Heights { get; init; }
        public required Vector3[] Normals { get; init; }
        public bool Changed { get; set; }
        /// <summary>The box its ground filled as the game had it (what the editor's picture of it is spread over).</summary>
        public required Vector3 DrawnMin { get; init; }
        public required Vector3 DrawnMax { get; init; }
        /// <summary>Made over at every grid step here (see <see cref="FullDetail"/>): the game has the old layout until the level is loaded again.</summary>
        public bool Regridded { get; init; }
        internal Dictionary<(int, int), List<int>> Buckets { get; } = [];
    }

    const float BucketSize = 8;  // metres: vertices sorted into squares, to find those under the brush

    public string Zone { get; }
    public string FieldName { get; }
    readonly byte[] fieldFile;
    readonly TerrainHeightField field;
    float[] samples;
    readonly Vector2 corner;
    readonly int columns, rows;
    public float Step { get; }
    readonly List<Part> parts;
    public IReadOnlyList<Part> Parts => parts;
    public bool FieldChanged { get; private set; }

    /// <summary>Whether anything has been sculpted since it was read (or last saved).</summary>
    public bool Changed => FieldChanged || Parts.Any(p => p.Changed);

    // The far-away ground (the terrain file's low-detail meshes, their buffers in the .gterrain_pc),
    // moved with the field when saved (see FarGround.Reheight): from the field as it was read.
    public string FarName => Zone + ".gterrain_pc";
    readonly byte[]? farFile;
    readonly float[] readSamples;
    /// <summary>Why the far-away ground can't follow (no .gterrain_pc, or its terrain file doesn't read back the same), or null.</summary>
    public string? FarProblem { get; private set; }
    /// <summary>How many far vertices, road vertices and foliage sub-cubes the last <see cref="Files"/> moved, and foliage sub-cubes it had to leave out.</summary>
    public int FarMoved { get; private set; }
    public int RoadsMoved { get; private set; }
    public int FoliageMoved { get; private set; }
    public int FoliageLost { get; private set; }
    float[] savedSamples;  // the field as last saved (as read, at first): how far the ground moved since, for what stands on it

    TerrainSculpt(string zone, string fieldName, byte[] fieldFile, TerrainHeightField field, Vector2 corner, List<Part> parts, byte[]? farFile)
    {
        Zone = zone;
        FieldName = fieldName;
        this.fieldFile = fieldFile;
        this.field = field;
        samples = field.Heights(fieldFile);
        readSamples = (float[])samples.Clone();
        savedSamples = (float[])samples.Clone();
        this.farFile = farFile;
        if (farFile == null) FarProblem = $"{FarName} isn't in the game's files";
        else
        {
            try
            {
                TerrainFile read = TerrainFile.Read(fieldFile);
                if (!read.Write().AsSpan().SequenceEqual(fieldFile)) FarProblem = $"{fieldName} doesn't write back the same";
                else if (read.FarRecords == null || read.FarBlocks.Count != 9) FarProblem = $"{fieldName} has no far meshes";
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentException or IndexOutOfRangeException)
            {
                FarProblem = $"{fieldName} couldn't be read whole ({ex.Message})";
            }
        }
        this.corner = corner;
        columns = field.XSamples;
        rows = field.ZSamples;
        Step = field.Step;
        this.parts = parts;
        foreach (Part part in parts) Bucket(part);
    }

    static void Bucket(Part part)
    {
        for (int v = 0; v < part.Subzone.Positions.Length; v++)
        {
            Vector3 p = part.Subzone.Positions[v];
            var key = ((int)MathF.Floor(p.X / BucketSize), (int)MathF.Floor(p.Z / BucketSize));
            if (!part.Buckets.TryGetValue(key, out List<int>? list)) part.Buckets[key] = list = [];
            list.Add(v);
        }
    }

    bool fullDetail;

    /// <summary>What <see cref="FullDetail"/> did, until the editor takes it (to say so, and draw the sub-zones made over).</summary>
    public string? DetailNote { get; private set; }

    /// <summary>Makes the drawn ground the collision now (see <see cref="FullDetail"/>), as the first stroke would.</summary>
    public void MakeFullDetail() => DetailNote ??= FullDetail();

    /// <summary>Takes <see cref="DetailNote"/> (once).</summary>
    public string? TakeDetailNote()
    {
        string? note = DetailNote;
        if (note != null) DetailNote = "";
        return note is { Length: > 0 } ? note : null;
    }

    /// <summary>
    /// True from <see cref="FullDetail"/> making a sub-zone over until the game has loaded the level
    /// again (<see cref="ReloadedInGame"/>): till then the game has the old layout, so the ground
    /// can't be changed in it live.
    /// </summary>
    public bool LayoutPending { get; private set; }

    /// <summary>
    /// Makes the drawn ground the collision itself, before the first stroke: the game's sub-zones are
    /// simplified from the height field (big flat triangles where it was flat), and a vertex only moves
    /// to the field's height where it stands, so a stroke between vertices showed in the collision
    /// alone (the player walked on a hill that wasn't drawn, or through one that was). Each sub-zone
    /// coarser than the field is made over with a vertex at every sample, its squares split as
    /// Havok splits the field's (see <see cref="TerrainSubzone.Regridded"/>). Its files grow (saved
    /// with the asset lists' sizes), and the game shows it once the level is loaded again. Returns
    /// what was done, or null if nothing was (once per zone).
    /// </summary>
    public string? FullDetail()
    {
        if (fullDetail) return null;
        fullDetail = true;
        int made = 0, before = 0, after = 0;
        var notes = new List<string>();
        for (int i = 0; i < parts.Count; i++)
        {
            Part part = parts[i];
            TerrainSubzone sub = part.Subzone;
            // The sub-zone's grid against the field's: the same step, its origin on a sample.
            float fx = (sub.Origin.X - corner.X) / Step, fz = (sub.Origin.Y - corner.Y) / Step;
            int ox = (int)MathF.Round(fx), oz = (int)MathF.Round(fz);
            if (MathF.Abs(sub.Step - Step) > 1e-4f || MathF.Abs(fx - ox) > 0.02f || MathF.Abs(fz - oz) > 0.02f)
            {
                notes.Add($"{part.HeaderName}'s grid isn't the field's");
                continue;
            }
            float Height(int x, int z) => samples[Math.Clamp(oz + z, 0, rows - 1) * columns + Math.Clamp(ox + x, 0, columns - 1)];
            Vector3 Normal(int x, int z) => NormalAt(corner.X + (ox + x) * Step, corner.Y + (oz + z) * Step);
            (byte[] Header, byte[] Data)? redone;
            try { redone = TerrainSubzone.Regridded(part.Header, part.Data, Height, Normal, out string? why); if (redone == null) notes.Add($"{part.HeaderName}: {why}"); }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException or IndexOutOfRangeException)
            {
                notes.Add($"{part.HeaderName}: {ex.Message}");
                continue;
            }
            if (redone is not { } files) continue;
            TerrainSubzone grid = TerrainSubzone.Read(files.Header, files.Data);
            if (grid.Triangles.Length <= sub.Triangles.Length) continue;  // (already at every step)
            before += sub.Triangles.Length / 3;
            after += grid.Triangles.Length / 3;
            var next = new Part
            {
                Number = part.Number, HeaderName = part.HeaderName, DataName = part.DataName, Header = files.Header, Data = files.Data, Subzone = grid,
                Heights = grid.Positions.Select(p => p.Y).ToArray(), Normals = (Vector3[])grid.Normals.Clone(),
                DrawnMin = part.DrawnMin, DrawnMax = part.DrawnMax, Regridded = true, Changed = true,
            };
            Bucket(next);
            Follow(next, null);
            next.Changed = true;
            parts[i] = next;
            made++;
        }
        if (made > 0) LayoutPending = true;
        if (made == 0 && notes.Count == 0) return null;
        return (made > 0 ? $"{Zone}: {made} sub-zones drawn at every grid step so the ground drawn is the collision ({before:N0} triangles before, {after:N0} now)" : $"{Zone}: the drawn ground stays as it was")
               + (notes.Count > 0 ? $" ({string.Join("; ", notes)})" : "");
    }

    /// <summary>
    /// The game has loaded the level again, from what was last saved: its layout is the editor's from
    /// now on. Returns the files as saved (what the game has now), for Live to change from.
    /// </summary>
    public IReadOnlyDictionary<string, byte[]> ReloadedInGame()
    {
        if (!Changed) LayoutPending = false;
        return lastSaved;
    }

    Dictionary<string, byte[]> lastWritten = new(StringComparer.OrdinalIgnoreCase), lastSaved = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The zone's ground for sculpting, or null with why: it needs the terrain file with a height
    /// field, and sub-zones of Armageddon's format (the game's, or the Guerrilla world as built).
    /// <paramref name="read"/> gives a file's bytes by name (from where the game reads it).
    /// </summary>
    public static TerrainSculpt? Load(string zone, Func<string, byte[]?> read, out string? why)
    {
        why = null;
        string fieldName = zone + ".cterrain_pc";
        if (read(fieldName) is not byte[] terrainFile)
        {
            why = $"{fieldName} isn't in the game's files";
            return null;
        }
// Read the terrain file to get the corner (where the height field's samples start) from its Data section.
        // This is at terrain_data +0x04 and +0x0C, which the game uses. The sub-zone origins may not align
        // perfectly with the grid (e.g., Guerrilla world zones), so deriving from them causes a
        // terrain/collision mismatch.
        TerrainFile terrain;
        try { terrain = TerrainFile.Read(terrainFile); }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentException or IndexOutOfRangeException)
        {
            why = $"{fieldName} couldn't be read ({ex.Message})";
            return null;
        }
        if (TerrainHeightField.Find(terrainFile) is not TerrainHeightField heightField)
        {
            why = $"{fieldName} has no ground to sculpt (a cave zone?)";
            return null;
        }
        var parts = new List<Part>();
        for (int number = 0; number < 9; number++)
        {
            string headerName = TerrainSubzone.HeaderFileName(zone, number), dataName = TerrainSubzone.DataFileName(zone, number);
            if (read(headerName) is not byte[] header || read(dataName) is not byte[] data) continue;
            TerrainSubzone subzone;
            try { subzone = TerrainSubzone.Read(header, data); }
            catch (Exception ex) when (ex is System.IO.InvalidDataException or NotSupportedException) { continue; }
            if (!subzone.HasGround) continue;
            if (subzone.FormatVersion != TerrainSubzone.Version)
            {
                why = $"{headerName} is in Red Faction: Guerrilla's format, which is only read";
                return null;
            }
            parts.Add(new Part
            {
                Number = number, HeaderName = headerName, DataName = dataName, Header = header, Data = data, Subzone = subzone,
                Heights = subzone.Positions.Select(p => p.Y).ToArray(), Normals = (Vector3[])subzone.Normals.Clone(),
                DrawnMin = subzone.BoundsMin, DrawnMax = subzone.BoundsMax,
            });
        }
        if (parts.Count == 0)
        {
            why = $"{zone} has no sub-zones with ground";
            return null;
        }
var corner = terrain.Corner;
        return new TerrainSculpt(zone, fieldName, terrainFile, heightField, corner, parts, read(zone + ".gterrain_pc"));
    }

    /// <summary>Whether a place (x, z) is over the zone's ground.</summary>
    public bool Covers(float x, float z)
    {
        float fx = (x - corner.X) / Step, fz = (z - corner.Y) / Step;
        return fx >= 0 && fz >= 0 && fx <= columns - 1 && fz <= rows - 1;
    }

    /// <summary>The ground's height at a place (x, z), between the four samples round it (the zone's edge beyond it).</summary>
    public float HeightAt(float x, float z) => HeightIn(samples, x, z);

    /// <summary>A height between the four samples round a place, in a copy of the field's samples (the zone's edge beyond it).</summary>
    float HeightIn(float[] heights, float x, float z)
    {
        float fx = Math.Clamp((x - corner.X) / Step, 0, columns - 1), fz = Math.Clamp((z - corner.Y) / Step, 0, rows - 1);
        int x0 = Math.Min((int)fx, columns - 2), z0 = Math.Min((int)fz, rows - 2);
        float tx = fx - x0, tz = fz - z0;
        float a = heights[z0 * columns + x0], b = heights[z0 * columns + x0 + 1];
        float c = heights[(z0 + 1) * columns + x0], d = heights[(z0 + 1) * columns + x0 + 1];
        return (a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + d * tx) * tz;
    }

    /// <summary>The ground's height at a place as it was when last saved (as read, before the first save).</summary>
    public float SavedHeightAt(float x, float z) => HeightIn(savedSamples, x, z);

    /// <summary>Whether a circle (x, z; radius) reaches over the zone.</summary>
    public bool Reaches(Vector2 centre, float radius) =>
        centre.X + radius >= corner.X && centre.Y + radius >= corner.Y &&
        centre.X - radius <= corner.X + (columns - 1) * Step && centre.Y - radius <= corner.Y + (rows - 1) * Step;

    /// <summary>
    /// One step of a stroke over every zone it reaches (see <see cref="Apply"/>), so it carries on
    /// across their edges: samples on a shared edge are at the same places in both zones, so they
    /// move the same; Smooth evens each out with its neighbours wherever they are, in this zone or
    /// the next. Returns each zone's parts that moved.
    /// </summary>
    public static List<(TerrainSculpt Zone, IReadOnlyList<Part> Parts)> ApplyAll(IReadOnlyList<TerrainSculpt> zones, Vector2 centre, float radius, SculptMode mode, float amount, float flattenTo = 0,
                                                                              bool opposite = false)
    {
        var reached = zones.Where(z => z.Reaches(centre, radius)).ToList();
        Func<float, float, float?>? before = null;
        if (mode == SculptMode.Smooth)
        {
            // Everyone's samples as they were before this step.
            var copies = reached.Select(z => (Zone: z, Heights: (float[])z.samples.Clone())).ToList();
            before = (x, z) => copies.FirstOrDefault(c => c.Zone.Covers(x, z)) is { Zone: not null } found ? (float?)found.Zone.HeightIn(found.Heights, x, z) : null;
        }
        var moved = new List<(TerrainSculpt, IReadOnlyList<Part>)>();
        foreach (TerrainSculpt zone in reached)
        {
            IReadOnlyList<Part> parts = zone.Apply(centre, radius, mode, amount, flattenTo, before, opposite);
            if (zone.lastApplyMoved) moved.Add((zone, parts));
        }
        return moved;
    }

    bool lastApplyMoved;

    Vector3 NormalAt(float x, float z)
    {
        float s = Step;
        float dx = (HeightAt(x + s, z) - HeightAt(x - s, z)) / (2 * s), dz = (HeightAt(x, z + s) - HeightAt(x, z - s)) / (2 * s);
        return Vector3.Normalize(new Vector3(-dx, 1, -dz));
    }

    /// <summary>
    /// Where a line (in the game's space) first meets the ground, within <paramref name="reach"/>
    /// metres; null if it doesn't over the zone. Marched in half-metre steps, then narrowed down.
    /// </summary>
    public Vector3? Hit(Vector3 origin, Vector3 direction, float reach = 2000)
    {
        direction = Vector3.Normalize(direction);
        float previous = 0;
        bool wasAbove = origin.Y > HeightAt(origin.X, origin.Z) || !Covers(origin.X, origin.Z);
        for (float t = 0.5f; t <= reach; t += t < 100 ? 0.5f : 2f)
        {
            Vector3 p = origin + direction * t;
            if (!Covers(p.X, p.Z)) { wasAbove = true; previous = t; continue; }
            bool above = p.Y > HeightAt(p.X, p.Z);
            if (wasAbove && !above)
            {
                float low = previous, high = t;
                for (int i = 0; i < 20; i++)
                {
                    float mid = (low + high) / 2;
                    Vector3 m = origin + direction * mid;
                    if (m.Y > HeightAt(m.X, m.Z)) low = mid; else high = mid;
                }
                Vector3 hit = origin + direction * high;
                return new Vector3(hit.X, HeightAt(hit.X, hit.Z), hit.Z);
            }
            wasAbove = above;
            previous = t;
        }
        return null;
    }

    /// <summary>Where the height field starts (x, z: the zone's corner).</summary>
    public Vector2 Corner => corner;

    /// <summary>How far the height field reaches from its corner, both ways (metres).</summary>
    public float Width => (columns - 1) * Step;

    /// <summary>
    /// Gives every sample of the field a new height: <paramref name="shape"/> (x, z, the height now)
    /// gives it, held to what the field can store; the ground follows. Returns the parts that changed
    /// (none if nothing did). The terrain creator and the road painter shape the ground with it.
    /// </summary>
    public IReadOnlyList<Part> Reshape(Func<float, float, float, float> shape)
    {
        DetailNote ??= FullDetail();
        float[] next = (float[])samples.Clone();
        bool any = false;
        float low = field.Offset, high = field.Offset + field.Scale * ushort.MaxValue;
        for (int z = 0; z < rows; z++)
            for (int x = 0; x < columns; x++)
            {
                int i = z * columns + x;
                float h = Math.Clamp(shape(corner.X + x * Step, corner.Y + z * Step, samples[i]), low, high);
                if (float.IsNaN(h) || MathF.Abs(h - samples[i]) < 1e-4f) continue;
                next[i] = h;
                any = true;
            }
        return any ? Restore(next) : [];
    }

    /// <summary>The lowest and highest heights the field can store.</summary>
    public (float Low, float High) Range => (this.field.Offset, this.field.Offset + this.field.Scale * ushort.MaxValue);

    /// <summary>Smooth noise from 0 to 1 (see <see cref="ValueNoise"/>), for the terrain creator.</summary>
    internal static float Noise(float x, float z, int seed) => ValueNoise(x, z, seed);

    /// <summary>The height field's samples now (a copy), for undoing a stroke.</summary>
    public float[] Snapshot() => (float[])samples.Clone();

    /// <summary>Puts the samples back as a snapshot had them, and the ground with them. Returns the parts that changed.</summary>
    public IReadOnlyList<Part> Restore(float[] snapshot)
    {
        samples = (float[])snapshot.Clone();
        FieldChanged = true;
        foreach (Part part in Parts) Follow(part, null);
        return Parts;
    }

    /// <summary>
    /// One step of a stroke at <paramref name="centre"/> (x and z; the game's space): the samples
    /// within <paramref name="radius"/> metres move by the mode, most in the middle and none at the
    /// edge (a smooth fall-off). Raise and Lower move them <paramref name="amount"/> metres in the
    /// middle; Smooth and Flatten take them that share of the way (0 to 1) to their neighbours'
    /// average or to <paramref name="flattenTo"/>. Returns the parts whose vertices moved.
    /// </summary>
    public IReadOnlyList<Part> Apply(Vector2 centre, float radius, SculptMode mode, float amount, float flattenTo = 0, Func<float, float, float?>? smoothFrom = null,
                                     bool opposite = false)
    {
        lastApplyMoved = false;
        if (mode >= SculptMode.PaintFoliage) return [];
        if (radius <= 0 || amount <= 0) return [];
        DetailNote ??= FullDetail();
        int xFrom = Math.Max(0, (int)MathF.Floor((centre.X - radius - corner.X) / Step)), xTo = Math.Min(columns - 1, (int)MathF.Ceiling((centre.X + radius - corner.X) / Step));
        int zFrom = Math.Max(0, (int)MathF.Floor((centre.Y - radius - corner.Y) / Step)), zTo = Math.Min(rows - 1, (int)MathF.Ceiling((centre.Y + radius - corner.Y) / Step));
        if (xFrom > xTo || zFrom > zTo) return [];
        float[]? before = mode == SculptMode.Smooth ? (float[])samples.Clone() : null;
        bool any = false;
        for (int z = zFrom; z <= zTo; z++)
            for (int x = xFrom; x <= xTo; x++)
            {
                float px = corner.X + x * Step, pz = corner.Y + z * Step;
                float t = Vector2.Distance(new Vector2(px, pz), centre) / radius;
                if (t >= 1) continue;
                float weight = (1 - t * t) * (1 - t * t);
                int i = z * columns + x;
                float h = samples[i];
                switch (mode)
                {
                    case SculptMode.Raise: h += amount * weight; break;
                    case SculptMode.Lower: h -= amount * weight; break;
                    case SculptMode.Hill:
                    case SculptMode.Peak:
                    case SculptMode.Mountain:
                    case SculptMode.Valley:
                        h += (opposite ^ mode == SculptMode.Valley ? -1 : 1) * amount * Shape(mode, t, px, pz, radius);
                        break;
                    case SculptMode.Flatten: h += (flattenTo - h) * Math.Clamp(amount * weight, 0, 1); break;
                    case SculptMode.Mesa:
                    case SculptMode.Canyon:
                    case SculptMode.Dunes:
                    case SculptMode.Crater:
                    case SculptMode.Erode:
                    case SculptMode.Roughen:
                        h += (opposite ? -1 : 1) * amount * Martian(mode, t, px, pz, radius);
                        break;
                    case SculptMode.Terrace:
                    {
                        // Ledges: each step of the strata flat, then a steep rise to the next.
                        float step = MathF.Max(1, radius / (opposite ? 3 : 6));
                        float level = h / step, floor = MathF.Floor(level), part = level - floor;
                        float ledged = (floor + SmoothStep(0.65f, 1, part)) * step;
                        h += (ledged - h) * Math.Clamp(amount * weight * 0.5f, 0, 1);
                        break;
                    }
                    case SculptMode.Cliff:
                    {
                        // Away from the height the stroke began at (toward it with Shift): a sharp edge there.
                        float d = h - flattenTo;
                        float push = MathF.Sign(d) * MathF.Min(1, MathF.Abs(d) / 2) * amount * weight;
                        h += opposite ? -MathF.Sign(d) * MathF.Min(MathF.Abs(d), amount * weight) : push;
                        break;
                    }
                    case SculptMode.Smooth:
                    {
                        float sum = 0;
                        int count = 0;
                        for (int dz = -2; dz <= 2; dz++)
                            for (int dx = -2; dx <= 2; dx++)
                            {
                                int sx = x + dx, sz = z + dz;
                                if (sx < 0 || sz < 0 || sx >= columns || sz >= rows)
                                {
                                    // Past this zone's edge: the next zone's, if it is being sculpted too.
                                    if (smoothFrom?.Invoke(px + dx * Step, pz + dz * Step) is float next) { sum += next; count++; }
                                    continue;
                                }
                                sum += smoothFrom?.Invoke(px + dx * Step, pz + dz * Step) ?? before![sz * columns + sx];
                                count++;
                            }
                        h += (sum / count - h) * Math.Clamp(amount * weight, 0, 1);
                        break;
                    }
                }
                // What the field can hold (its samples are u16 from its offset, by its scale).
                h = Math.Clamp(h, field.Offset, field.Offset + field.Scale * ushort.MaxValue);
                if (h != samples[i]) { samples[i] = h; any = true; }
            }
        if (!any) return [];
        FieldChanged = true;
        lastApplyMoved = true;
        var area = (Min: new Vector2(corner.X + xFrom * Step, corner.Y + zFrom * Step) - new Vector2(Step), Max: new Vector2(corner.X + xTo * Step, corner.Y + zTo * Step) + new Vector2(Step));
        var moved = new List<Part>();
        foreach (Part part in Parts)
            if (Follow(part, area)) moved.Add(part);
        return moved;
    }

    /// <summary>
    /// How much of a stroke step the ground gets at a place (0 to 1) for the landform modes, by how far
    /// out it is (t: 0 in the middle, 1 at the brush's edge): Hill a cosine bell; Peak steep from the
    /// middle (cubed); Mountain the bell broken up by ridged noise (three octaves, sized to the brush,
    /// the same at a place every time, so strokes build on each other); Valley a trough, flat across
    /// most of the brush and steep at its sides.
    /// </summary>
    static float Shape(SculptMode mode, float t, float x, float z, float radius)
    {
        float bell = 0.5f * (1 + MathF.Cos(MathF.PI * t));
        switch (mode)
        {
            case SculptMode.Peak:
                return MathF.Pow(1 - t, 3);
            case SculptMode.Mountain:
            {
                float ridges = 0, size = MathF.Max(radius * 0.6f, 2), weight = 0.6f, total = 0;
                for (int octave = 0; octave < 3; octave++, size *= 0.5f, weight *= 0.5f)
                {
                    float n = ValueNoise(x / size, z / size, octave);
                    ridges += (1 - MathF.Abs(n * 2 - 1)) * weight;
                    total += weight;
                }
                return bell * (0.35f + 0.65f * ridges / total) * (0.6f + 0.4f * MathF.Pow(1 - t, 2));
            }
            case SculptMode.Valley:
            {
                float side = Math.Clamp((t - 0.55f) / 0.45f, 0, 1);
                return 0.5f * (1 + MathF.Cos(MathF.PI * side));
            }
            default:
                return bell;
        }
    }

    static float SmoothStep(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// How much a step of the Martian modes raises the ground at a place (lowers, below 0), by how far
    /// out it is (t: 0 in the middle, 1 at the brush's edge): see <see cref="SculptMode"/>.
    /// </summary>
    static float Martian(SculptMode mode, float t, float x, float z, float radius)
    {
        float size = MathF.Max(radius, 2);
        switch (mode)
        {
            case SculptMode.Mesa:
            {
                // A flat top to about two thirds out, its edge broken up, then a steep fall.
                float edge = t + (ValueNoise(x / (size * 0.35f), z / (size * 0.35f), 21) - 0.5f) * 0.25f;
                return 1 - SmoothStep(0.55f, 0.72f, edge);
            }
            case SculptMode.Canyon:
            {
                // A floor a quarter across, then walls stepping up in four ledges to the rim.
                float side = Math.Clamp((t - 0.22f) / 0.78f, 0, 1) * 4;
                float ledge = MathF.Floor(side), part = side - ledge;
                float up = (ledge + SmoothStep(0.55f, 1, part)) / 4;
                return -(1 - up);
            }
            case SculptMode.Dunes:
            {
                // Ripples across the wind (along x, bent by noise): a long gentle side and a steep lee.
                float u = x / (size * 0.35f) + (ValueNoise(x / size, z / size, 31) - 0.5f) * 2.5f + z / (size * 2.5f);
                float wave = u - MathF.Floor(u);
                float profile = wave < 0.75f ? wave / 0.75f : (1 - wave) / 0.25f;
                float fade = (1 - t * t) * (1 - t * t);
                return (0.5f - 0.5f * MathF.Cos(MathF.PI * profile) - 0.35f) * fade;
            }
            case SculptMode.Crater:
            {
                float bowl = t < 0.75f ? -(1 - (t / 0.75f) * (t / 0.75f)) : 0;
                float rim = 0.55f * MathF.Exp(-MathF.Pow((t - 0.8f) / 0.12f, 2));
                return bowl + rim;
            }
            case SculptMode.Erode:
            {
                // Gullies: the creases of ridged noise, two sizes, sharpened; deepest in the middle.
                float gullies = 0;
                foreach ((float scale, float weight, int seed) in new[] { (0.45f, 0.65f, 41), (0.18f, 0.35f, 42) })
                {
                    float n = ValueNoise(x / (size * scale), z / (size * scale), seed);
                    gullies += MathF.Pow(1 - MathF.Abs(n * 2 - 1), 6) * weight;
                }
                return -gullies * (1 - t * t);
            }
            case SculptMode.Roughen:
            {
                float n = 0, w = 0.6f;
                for (int octave = 0; octave < 3; octave++, w *= 0.5f)
                    n += (ValueNoise(x / (size * 0.12f) * (1 << octave), z / (size * 0.12f) * (1 << octave), 51 + octave) - 0.5f) * w;
                return n * (1 - t * t) * 0.8f;
            }
            default:
                return 0;
        }
    }

    /// <summary>Smooth noise from 0 to 1 at a place (value noise on a 1-unit grid, smoothly blended), different for each seed.</summary>
    static float ValueNoise(float x, float z, int seed)
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

    /// <summary>The part's vertices (within an area, or all) take the field's height and slope where they stand. Returns whether any moved.</summary>
    bool Follow(Part part, (Vector2 Min, Vector2 Max)? area)
    {
        bool any = false;
        void Vertex(int v)
        {
            Vector3 p = part.Subzone.Positions[v];
            if (area is { } box && (p.X < box.Min.X || p.X > box.Max.X || p.Z < box.Min.Y || p.Z > box.Max.Y)) return;
            float h = HeightAt(p.X, p.Z);
            if (MathF.Abs(h - part.Heights[v]) < 1e-4f && area != null) return;
            part.Heights[v] = h;
            part.Normals[v] = NormalAt(p.X, p.Z);
            any = true;
        }
        if (area is { } region)
        {
            for (int bz = (int)MathF.Floor(region.Min.Y / BucketSize); bz <= (int)MathF.Floor(region.Max.Y / BucketSize); bz++)
                for (int bx = (int)MathF.Floor(region.Min.X / BucketSize); bx <= (int)MathF.Floor(region.Max.X / BucketSize); bx++)
                    if (part.Buckets.TryGetValue((bx, bz), out List<int>? list))
                        foreach (int v in list) Vertex(v);
        }
        else
            for (int v = 0; v < part.Heights.Length; v++) Vertex(v);
        if (any) part.Changed = true;
        return any;
    }

    /// <summary>
    /// The files as the running game needs them for Live (see LevelEditor.LiveTerrain.cs): the terrain
    /// file with the heights (not its far-away ground or foliage) and each sub-zone sculpted with its
    /// ground; the files as read for the rest. Lighter than <see cref="Files"/>, for every few frames.
    /// </summary>
    public Dictionary<string, byte[]> LiveFiles()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        if (FieldChanged)
        {
            byte[] withHeights = field.WithHeights(fieldFile, samples);
            files[FieldName] = withHeights;
            // The far-away ground with it (as Files does), so it doesn't show the old shape between the near ground's edges.
            if (FarProblem == null && farFile != null)
            {
                try
                {
                    TerrainFile terrain = TerrainFile.Read(withHeights);
                    (byte[] far, _) = RfaFormats.Guerrilla.FarGround.Reheight(terrain, farFile, (x, z) => HeightIn(readSamples, x, z), HeightAt);
                    byte[] written = terrain.Write();
                    if (written.Length == withHeights.Length)
                    {
                        files[FieldName] = written;
                        files[FarName] = far;
                    }
                }
                catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentException or IndexOutOfRangeException) { }
            }
        }
        foreach (Part part in Parts.Where(p => p.Changed))
        {
            (byte[] header, byte[] data) = TerrainSubzone.WithGround(part.Header, part.Data, part.Heights, part.Normals);
            files[part.HeaderName] = header;
            files[part.DataName] = data;
        }
        return files;
    }

    /// <summary>A file of this zone's ground as it was read (the game has it so until it is changed live or the level loaded again), or null.</summary>
    public byte[]? ReadFile(string name)
    {
        if (name.Equals(FieldName, StringComparison.OrdinalIgnoreCase)) return fieldFile;
        if (name.Equals(FarName, StringComparison.OrdinalIgnoreCase)) return farFile;
        foreach (Part part in Parts)
        {
            if (name.Equals(part.HeaderName, StringComparison.OrdinalIgnoreCase)) return part.Header;
            if (name.Equals(part.DataName, StringComparison.OrdinalIgnoreCase)) return part.Data;
        }
        return null;
    }

    /// <summary>The changed files, by name: the terrain file and the sub-zones sculpted.</summary>
    public Dictionary<string, byte[]> Files()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        FarMoved = RoadsMoved = FoliageMoved = FoliageLost = 0;
        // How far the ground moved at a place since it was read (the files below are as read).
        float Moved(float x, float z) => HeightAt(x, z) - HeightIn(readSamples, x, z);
        if (FieldChanged)
        {
            byte[] withHeights = field.WithHeights(fieldFile, samples);
            files[FieldName] = withHeights;
            // The far-away ground with it, from the far meshes as read moved by how far the field moved since.
            if (FarProblem == null && farFile != null)
            {
                try
                {
                    TerrainFile terrain = TerrainFile.Read(withHeights);
                    (byte[] far, int moved) = RfaFormats.Guerrilla.FarGround.Reheight(terrain, farFile, (x, z) => HeightIn(readSamples, x, z), HeightAt);
                    byte[] written = terrain.Write();
                    if (written.Length == withHeights.Length)
                    {
                        files[FieldName] = written;
                        files[FarName] = far;
                        FarMoved = moved;
                    }
                    else FarProblem = $"{FieldName} would change size with its far meshes";
                }
                catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentException or IndexOutOfRangeException)
                {
                    FarProblem = ex.Message;
                }
            }
            // The foliage with it (its sub-cubes near the ground, in place: the file keeps its size).
            try
            {
                byte[] terrainFile = (byte[])files[FieldName].Clone();
                (FoliageMoved, FoliageLost) = ZoneFoliage.Follow(terrainFile, Zone, (x, z) => HeightIn(readSamples, x, z), HeightAt);
                if (FoliageMoved > 0 || FoliageLost > 0) files[FieldName] = terrainFile;
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentException or IndexOutOfRangeException)
            {
                FoliageMoved = FoliageLost = 0;
            }
        }
        foreach (Part part in Parts.Where(p => p.Changed))
        {
            (byte[] header, byte[] data) = TerrainSubzone.WithGround(part.Header, part.Data, part.Heights, part.Normals);
            // Its roads with it (both files keep their sizes).
            (header, data, int roads) = TerrainSubzone.WithRoadsMoved(header, data, Moved);
            RoadsMoved += roads;
            files[part.HeaderName] = header;
            files[part.DataName] = data;
        }
        lastWritten = new Dictionary<string, byte[]>(files, StringComparer.OrdinalIgnoreCase);
        return files;
    }

    /// <summary>After saving: what was written is what is on disk now (later strokes start from it).</summary>
    public void Saved()
    {
        savedSamples = (float[])samples.Clone();
        foreach (var (name, bytes) in lastWritten) lastSaved[name] = bytes;
        FieldChanged = false;
        foreach (Part part in Parts) part.Changed = false;
    }
}
