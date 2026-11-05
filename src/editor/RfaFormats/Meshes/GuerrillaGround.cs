using System.Numerics;

namespace RfaFormats.Meshes;

/// <summary>
/// How a zone made from Guerrilla's ground is sampled. Guerrilla's zones are 511 m squares sampled
/// every 1 m. <see cref="Quarter"/>: Armageddon's own zones, 255 m squares sampled every 255/511 m,
/// so one of Guerrilla's steps becomes two of Armageddon's (the land 0.2% smaller) and a 255.5 m
/// square of Guerrilla's world fills a zone: 257 samples, every 1 m from 0 to 255 m, then the far edge
/// at 255.5 m (the zone's 511th step), so that zones made from neighbouring squares share their
/// edge samples. <see cref="Whole"/>: a whole Guerrilla zone, for a world the game gives 511 m zones
/// (as it does those named mpl_*): 512 samples 1 m apart, one grid step each, as Guerrilla's own.
/// </summary>
public sealed class GroundLayout
{
    /// <summary>Samples across the square.</summary>
    public int Samples { get; }

    /// <summary>The zone's grid steps from one sample to the next.</summary>
    public int StepsPerSample { get; }

    /// <summary>A grid step in the game's metres.</summary>
    public float GridStep { get; }

    readonly bool halfLast;  // the last sample half a step after the one before

    GroundLayout(int samples, int stepsPerSample, float gridStep, bool halfLast)
    {
        Samples = samples;
        StepsPerSample = stepsPerSample;
        GridStep = gridStep;
        this.halfLast = halfLast;
    }

    public static readonly GroundLayout Quarter = new(257, 2, TerrainSubzone.GridStep, true);
    public static readonly GroundLayout Whole = new(512, 1, TerrainSubzone.GuerrillaGridStep, false);

    /// <summary>Where sample <paramref name="n"/> is, in Guerrilla's metres from the square's corner.</summary>
    public float SampleAt(int n) => halfLast ? Math.Min(n, Samples - 1.5f) : n;

    /// <summary>Which of the zone's grid steps sample <paramref name="n"/> is on.</summary>
    public int StepOf(int n) => halfLast ? Math.Min(StepsPerSample * n, StepsPerSample * Samples - 3) : StepsPerSample * n;
}

/// <summary>
/// How a square of ground samples is drawn under Guerrilla's roads (see
/// <see cref="GuerrillaGround.UnderRoads"/>). Cut as usual, from (x, z + 1) to (x + 1, z), its first
/// triangle is the one with the corner at (x, z), its second the one with (x + 1, z + 1);
/// <see cref="Flipped"/>, from (x, z) to (x + 1, z + 1), its first is the one with (x + 1, z), its
/// second the one with (x, z + 1).
/// </summary>
[Flags]
public enum SquareCut : byte
{
    None = 0,
    First = 1,    // left out
    Second = 2,   // left out
    Flipped = 4,  // cut from (x, z) to (x + 1, z + 1), as Guerrilla cuts most of its squares
}

/// <summary>
/// Red Faction: Guerrilla's ground in place of an Armageddon zone's: its terrain sub-zones (what is
/// drawn) and the height field in its terrain file (what is walked on), sampled as a
/// <see cref="GroundLayout"/> says (<see cref="GroundLayout.Quarter"/> where none is given).
///
/// The ground is sampled from Guerrilla's sub-zones every 1 m (the top surface: their skirts hang
/// below the edges). It is made again as a grid of those samples: each of the zone's nine sub-zones
/// takes its third of the square both ways (neighbours share their edge samples, so there are no
/// cracks), cut into 5 × 5 patches, each patch a strip row by row. The height field takes the same
/// samples, the drawn ground's height in between.
/// </summary>
public static class GuerrillaGround
{
    /// <summary>Samples across a quarter-layout square (see <see cref="GroundLayout.Quarter"/>).</summary>
    public const int Samples = 257;

    /// <summary>Where sample <paramref name="n"/> of a quarter-layout square is, in Guerrilla's metres from its corner.</summary>
    public static float SampleAt(int n) => GroundLayout.Quarter.SampleAt(n);

    const int PatchesAcross = 5;

    /// <summary>
    /// Guerrilla's ground over the square from <paramref name="corner"/> (x, z in its world), from
    /// its sub-zones there: the top surface's height at each sample, <c>[z * Samples + x]</c>, its
    /// roads and paving included. Points no sub-zone covers are where Guerrilla's rocks close the
    /// ground themselves (caps on pillar tops, covers on slopes); they are filled smoothly from the
    /// covered ones around them, and then, where one of <paramref name="closers"/> (the rocks'
    /// triangles in the world, three corners each) lies just above that fill (its first surface
    /// there no more than <see cref="ClosedReach"/> up), raised to <see cref="ClosedBelow"/> under
    /// it, so the ground stays inside the rock with no gap under its rim. A rock surface higher up
    /// spans the hole (an arch, a tall spire's flank) rather than closing it, and is left alone.
    /// Null if nothing is covered.
    /// </summary>
    public static float[]? Sample(IEnumerable<TerrainSubzone> subzones, Vector2 corner, GroundLayout? layout = null, IEnumerable<Vector3>? closers = null)
    {
        layout ??= GroundLayout.Quarter;
        int samples = layout.Samples;
        var heights = Cover(subzones, corner, layout);
        if (heights.All(float.IsNaN)) return null;
        bool[] hole = heights.Select(float.IsNaN).ToArray();
        Fill(heights, hole, samples);
        if (closers == null) return heights;
        // The first rock surface at or above each filled sample's fill, if near enough.
        var under = new float[heights.Length];
        Array.Fill(under, float.NaN);
        using var corners = closers.GetEnumerator();
        while (corners.MoveNext())
        {
            var a = corners.Current;
            if (!corners.MoveNext()) break;
            var b = corners.Current;
            if (!corners.MoveNext()) break;
            Visit(a, b, corners.Current, corner, layout, (i, h) =>
            {
                if (hole[i] && h >= heights[i] - ClosedBelow && h <= heights[i] + ClosedReach && (float.IsNaN(under[i]) || h < under[i])) under[i] = h;
            });
        }
        bool closed = false;
        for (int i = 0; i < heights.Length; i++)
            if (!float.IsNaN(under[i]))
            {
                heights[i] = MathF.Max(heights[i], under[i] - ClosedBelow);
                hole[i] = false;
                closed = true;
            }
        // The rest of each hole smoothed again around what the rocks closed.
        if (closed) Smooth(heights, hole, samples);
        return heights;
    }

    /// <summary>The samples marked as holes filled: first the nearest covered sample's height, spreading out a step at a time; then smoothed.</summary>
    static void Fill(float[] heights, bool[] hole, int samples)
    {
        while (heights.Any(float.IsNaN))
        {
            float[] before = (float[])heights.Clone();
            for (int z = 0; z < samples; z++)
                for (int x = 0; x < samples; x++)
                {
                    if (!float.IsNaN(before[z * samples + x])) continue;
                    foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                        if (x + dx >= 0 && x + dx < samples && z + dz >= 0 && z + dz < samples && !float.IsNaN(before[(z + dz) * samples + x + dx]))
                        {
                            heights[z * samples + x] = before[(z + dz) * samples + x + dx];
                            break;
                        }
                }
        }
        Smooth(heights, hole, samples);
    }

    /// <summary>
    /// Each hole sample made the mean of its neighbours, over and over, so a hole spans its sides
    /// smoothly (across a slope) instead of stepping between them where the spreading from each
    /// side met.
    /// </summary>
    static void Smooth(float[] heights, bool[] hole, int samples)
    {
        int[] filled = Enumerable.Range(0, hole.Length).Where(i => hole[i]).ToArray();
        for (int pass = 0; pass < HoleSmoothing; pass++)
            foreach (int i in filled)
            {
                int x = i % samples, z = i / samples;
                float sum = 0;
                int count = 0;
                if (x > 0) { sum += heights[i - 1]; count++; }
                if (x < samples - 1) { sum += heights[i + 1]; count++; }
                if (z > 0) { sum += heights[i - samples]; count++; }
                if (z < samples - 1) { sum += heights[i + samples]; count++; }
                heights[i] = sum / count;
            }
    }

    /// <summary>How many times holes are smoothed (enough to settle across a hole a few dozen metres wide).</summary>
    const int HoleSmoothing = 400;

    /// <summary>How far below a rock's surface the ground goes where the rock closes a hole (m).</summary>
    public const float ClosedBelow = 0.5f;

    /// <summary>How far above a hole's smooth fill a rock's surface may be and still close the hole (m); the floating rocks' rims were 1–4.5 m up.</summary>
    public const float ClosedReach = 6f;

    /// <summary>Which samples of the square (as <see cref="Sample"/>'s) no sub-zone covers.</summary>
    public static bool[] Uncovered(IEnumerable<TerrainSubzone> subzones, Vector2 corner, GroundLayout? layout = null) =>
        Cover(subzones, corner, layout).Select(float.IsNaN).ToArray();

    /// <summary>The top surface's height at each sample (the ground, and Guerrilla's roads and paving), NaN where no sub-zone covers it.</summary>
    public static float[] Cover(IEnumerable<TerrainSubzone> subzones, Vector2 corner, GroundLayout? layout = null)
    {
        layout ??= GroundLayout.Quarter;
        int samples = layout.Samples;
        var heights = new float[samples * samples];
        Array.Fill(heights, float.NaN);
        foreach (TerrainSubzone s in subzones.Where(s => s.HasGround))
        {
            for (int t = 0; t < s.Triangles.Length; t += 3) Lay(heights, s.Positions[s.Triangles[t]], s.Positions[s.Triangles[t + 1]], s.Positions[s.Triangles[t + 2]], corner, layout);
            for (int t = 0; t + 2 < s.Paving.Length; t += 3) Lay(heights, s.Paving[t], s.Paving[t + 1], s.Paving[t + 2], corner, layout);
        }
        return heights;
    }

    /// <summary>A triangle laid on the samples it covers: each keeps the highest surface over it.</summary>
    static void Lay(float[] heights, Vector3 a, Vector3 b, Vector3 c, Vector2 corner, GroundLayout layout) =>
        Visit(a, b, c, corner, layout, (i, h) => { if (float.IsNaN(heights[i]) || h > heights[i]) heights[i] = h; });

    /// <summary>Each sample a triangle covers, with the triangle's height there.</summary>
    static void Visit(Vector3 a, Vector3 b, Vector3 c, Vector2 corner, GroundLayout layout, Action<int, float> visit)
    {
        int samples = layout.Samples;
        float area = (b.X - a.X) * (c.Z - a.Z) - (c.X - a.X) * (b.Z - a.Z);
        if (MathF.Abs(area) < 1e-9f) return;
        int x1 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X)) - corner.X)), x2 = Math.Min(samples - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X)) - corner.X));
        int z1 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Z, MathF.Min(b.Z, c.Z)) - corner.Y)), z2 = Math.Min(samples - 1, (int)MathF.Ceiling(MathF.Max(a.Z, MathF.Max(b.Z, c.Z)) - corner.Y));
        for (int z = z1; z <= z2; z++)
            for (int x = x1; x <= x2; x++)
            {
                float px = corner.X + layout.SampleAt(x), pz = corner.Y + layout.SampleAt(z);
                float wa = ((b.X - px) * (c.Z - pz) - (c.X - px) * (b.Z - pz)) / area;
                float wb = ((c.X - px) * (a.Z - pz) - (a.X - px) * (c.Z - pz)) / area;
                float wc = 1 - wa - wb;
                if (wa < -1e-4f || wb < -1e-4f || wc < -1e-4f) continue;
                visit(z * samples + x, wa * a.Y + wb * b.Y + wc * c.Y);
            }
    }

    /// <summary>How finely <see cref="UnderRoads"/> looks at each square of samples: a point every eighth of its side.</summary>
    const int RoadLook = 8;

    /// <summary>How far under a road decal the drawn ground is put where the decal covers a sample (m).</summary>
    public const float UnderRoad = 0.05f;

    /// <summary>How far under a road decal a triangle of ground kept under it must stay everywhere (m); one that doesn't shows through.</summary>
    const float UnderRoadKept = 0.01f;

    /// <summary>The triangles of ground <see cref="UnderRoads"/> has looked at so far: kept under the decals, left out, and showing through by a decal's edge; for the builder's log.</summary>
    public static readonly long[] RoadTriangles = new long[3];

    /// <summary>
    /// The ground under Guerrilla's road decals, over the square from <paramref name="corner"/> with
    /// the samples' <paramref name="heights"/> (see <see cref="Sample"/>, whose top surface takes the
    /// decals' own height where they cover a sample). Guerrilla cuts its ground open under each decal
    /// and fills the rest of the way to the decal's edge with a skirt; Armageddon draws a road decal
    /// over what is already drawn at its depth or behind it, writing no depth itself. So our ground
    /// stays under the decals, <see cref="UnderRoad"/> under them at the samples they cover (see
    /// <see cref="BelowRoads"/>): where the road's light and shadow are worked out from what is drawn
    /// there, it is the ground just under the road. A triangle of it is left out only where a decal
    /// covers all of it (so no gap opens beside a road) and none of Guerrilla's own ground is there
    /// (the skirts' cut-outs, around rocks, stay filled), and only if the ground would still show
    /// through the decal somewhere (the decal curving below the straight triangle). Each square of
    /// samples may be cut either way (see <see cref="SquareCut"/>): the way that leaves the fewer
    /// triangles showing through where they can't be left out, then the fewer left out.
    /// Returns which triangles of each square (<c>[z * (Samples - 1) + x]</c>) to leave out, and the
    /// lowest decal's height at each sample (NaN where none), or null if no decal is over the square.
    /// </summary>
    public static (SquareCut[] Cuts, float[]? Surface) UnderRoads(IEnumerable<TerrainSubzone> subzones, Vector2 corner, GroundLayout layout, float[] heights)
    {
        int samples = layout.Samples, squares = samples - 1, points = squares * RoadLook + 1;
        var cuts = new SquareCut[squares * squares];
        var over = subzones.ToList();
        if (!over.Any(s => s.RoadDecals.Count > 0)) return (cuts, null);

        // The points: an eighth of a square apart (the quarter layout's last square is half a metre).
        var at = new float[points];
        for (int p = 0; p < points; p++)
        {
            int n = Math.Min(p / RoadLook, squares - 1);
            at[p] = layout.SampleAt(n) + (p - n * RoadLook) / (float)RoadLook * (layout.SampleAt(n + 1) - layout.SampleAt(n));
        }
        int PointBelow(float metres)
        {
            int n = Math.Min(SampleBelow(metres, layout), squares - 1);
            float f = (metres - layout.SampleAt(n)) / (layout.SampleAt(n + 1) - layout.SampleAt(n));
            return Math.Clamp(n * RoadLook + (int)MathF.Floor(f * RoadLook), 0, points - 1);
        }
        int SquareOf(int p) => Math.Min(p / RoadLook, squares - 1);
        // Each point a triangle covers (its edges included), with the triangle's height there; none if
        // <paramref name="wanted"/> says no to the squares the triangle spans.
        void Visit(Vector3 a, Vector3 b, Vector3 c, Func<int, int, int, int, bool>? wanted, Action<int, float> visit)
        {
            float area = (b.X - a.X) * (c.Z - a.Z) - (c.X - a.X) * (b.Z - a.Z);
            if (MathF.Abs(area) < 1e-9f) return;
            int x1 = PointBelow(MathF.Min(a.X, MathF.Min(b.X, c.X)) - corner.X), x2 = Math.Min(points - 1, PointBelow(MathF.Max(a.X, MathF.Max(b.X, c.X)) - corner.X) + 1);
            int z1 = PointBelow(MathF.Min(a.Z, MathF.Min(b.Z, c.Z)) - corner.Y), z2 = Math.Min(points - 1, PointBelow(MathF.Max(a.Z, MathF.Max(b.Z, c.Z)) - corner.Y) + 1);
            if (wanted != null && !wanted(SquareOf(x1), SquareOf(x2), SquareOf(z1), SquareOf(z2))) return;
            for (int z = z1; z <= z2; z++)
                for (int x = x1; x <= x2; x++)
                {
                    float px = corner.X + at[x], pz = corner.Y + at[z];
                    float wa = ((b.X - px) * (c.Z - pz) - (c.X - px) * (b.Z - pz)) / area;
                    float wb = ((c.X - px) * (a.Z - pz) - (a.X - px) * (c.Z - pz)) / area;
                    float wc = 1 - wa - wb;
                    if (wa < -1e-4f || wb < -1e-4f || wc < -1e-4f) continue;
                    visit(z * points + x, wa * a.Y + wb * b.Y + wc * c.Y);
                }
        }

        // The lowest decal's height at each point (NaN where none), and the squares any decal reaches.
        var road = new float[points * points];
        Array.Fill(road, float.NaN);
        var roadIn = new bool[squares * squares];
        foreach (TerrainSubzone s in over)
            foreach (GuerrillaRoadDecal decal in s.RoadDecals)
                for (int t = 0; t + 2 < decal.Corners.Length; t += 3)
                    Visit(decal.Corners[t], decal.Corners[t + 1], decal.Corners[t + 2], null, (k, h) =>
                    {
                        if (float.IsNaN(road[k]) || h < road[k]) road[k] = h;
                        roadIn[SquareOf(k / points) * squares + SquareOf(k % points)] = true;
                    });
        // The points Guerrilla's own ground is at too (only where a decal is).
        var ground = new bool[points * points];
        bool AnyRoad(int sx1, int sx2, int sz1, int sz2)
        {
            for (int z = sz1; z <= sz2; z++)
                for (int x = sx1; x <= sx2; x++)
                    if (roadIn[z * squares + x]) return true;
            return false;
        }
        foreach (TerrainSubzone s in over)
            for (int t = 0; t < s.Triangles.Length; t += 3)
                Visit(s.Positions[s.Triangles[t]], s.Positions[s.Triangles[t + 1]], s.Positions[s.Triangles[t + 2]], AnyRoad, (k, _) => ground[k] = true);

        // The lowest decal's height at each sample, and the drawn ground's there (see BelowRoads).
        var surface = new float[samples * samples];
        for (int z = 0; z < samples; z++)
            for (int x = 0; x < samples; x++) surface[z * samples + x] = road[z * RoadLook * points + x * RoadLook];
        var drawn = BelowRoads(heights, surface);

        for (int z = 0; z < squares; z++)
            for (int x = 0; x < squares; x++)
            {
                if (!roadIn[z * squares + x]) continue;
                float h00 = drawn[z * samples + x], h10 = drawn[z * samples + x + 1], h01 = drawn[(z + 1) * samples + x], h11 = drawn[(z + 1) * samples + x + 1];
                // A triangle of the square (its points u, v eighths along x and z): 0 under the decals
                // wherever they cover it, 1 to be left out, 2 showing through but not all covered.
                int Look(Func<int, int, bool> inside, Func<float, float, float> height)
                {
                    bool covered = true, through = false;
                    for (int v = 0; v <= RoadLook; v++)
                        for (int u = 0; u <= RoadLook; u++)
                        {
                            if (!inside(u, v)) continue;
                            int k = (z * RoadLook + v) * points + x * RoadLook + u;
                            if (float.IsNaN(road[k]) || ground[k]) { covered = false; continue; }
                            if (height(u / (float)RoadLook, v / (float)RoadLook) > road[k] - UnderRoadKept) through = true;
                        }
                    return !through ? 0 : covered ? 1 : 2;
                }
                // Cut as usual, from (x, z + 1) to (x + 1, z): the first triangle u + v <= 1, the second
                // u + v >= 1; flipped, from (x, z) to (x + 1, z + 1): the first u >= v, the second u <= v.
                int first = Look((u, v) => u + v <= RoadLook, (u, v) => h00 + u * (h10 - h00) + v * (h01 - h00));
                int second = Look((u, v) => u + v >= RoadLook, (u, v) => h11 + (1 - u) * (h01 - h11) + (1 - v) * (h10 - h11));
                int firstFlipped = Look((u, v) => u >= v, (u, v) => h10 + (1 - u) * (h00 - h10) + v * (h11 - h10));
                int secondFlipped = Look((u, v) => u <= v, (u, v) => h01 + u * (h11 - h01) + (1 - v) * (h00 - h01));
                static int Score(int a, int b) => (a == 2 ? 100 : a) + (b == 2 ? 100 : b);
                bool flip = Score(firstFlipped, secondFlipped) < Score(first, second);
                if (flip) (first, second) = (firstFlipped, secondFlipped);
                cuts[z * squares + x] = (flip ? SquareCut.Flipped : 0) | (first == 1 ? SquareCut.First : 0) | (second == 1 ? SquareCut.Second : 0);
                foreach (int look in new[] { first, second }) Interlocked.Increment(ref RoadTriangles[look]);
            }
        return (cuts, surface);
    }

    /// <summary>
    /// The drawn ground's heights: <paramref name="heights"/> (the samples), each sample a road decal
    /// covers put <see cref="UnderRoad"/> under the lowest decal there (<paramref name="surface"/>, see
    /// <see cref="UnderRoads"/>) if it isn't already. The height field keeps the samples as they are.
    /// </summary>
    public static float[] BelowRoads(float[] heights, float[]? surface)
    {
        var drawn = (float[])heights.Clone();
        if (surface != null)
            for (int i = 0; i < drawn.Length; i++)
                if (!float.IsNaN(surface[i])) drawn[i] = MathF.Min(drawn[i], surface[i] - UnderRoad);
        return drawn;
    }

    /// <summary>The sample at or before a distance (m) from the square's corner.</summary>
    static int SampleBelow(float metres, GroundLayout layout)
    {
        int n = Math.Clamp((int)MathF.Floor(metres / layout.SampleAt(1)), 0, layout.Samples - 1);
        while (n > 0 && layout.SampleAt(n) > metres) n--;
        while (n + 1 < layout.Samples && layout.SampleAt(n + 1) <= metres) n++;
        return n;
    }

    /// <summary>
    /// The height field's heights from the samples: one per grid step (in the quarter layout Guerrilla's
    /// at the even steps and the last, the drawn ground's height between them at the odd ones), laid out
    /// as <see cref="TerrainHeightField.Heights"/>, <paramref name="fieldSamples"/> across.
    /// </summary>
    public static float[] FieldHeights(float[] heights, int fieldSamples, GroundLayout layout)
    {
        var field = new float[fieldSamples * fieldSamples];
        for (int z = 0; z < fieldSamples; z++)
            for (int x = 0; x < fieldSamples; x++)
                field[z * fieldSamples + x] = HeightAt(heights, (float)x / layout.StepsPerSample, (float)z / layout.StepsPerSample, layout);
        return field;
    }

    /// <summary>
    /// The height at a point of the square (in Guerrilla's metres from its corner), on the
    /// triangles the sub-zones make: each square of samples is cut from its corner at (x, z + 1)
    /// to the one at (x + 1, z), so the height field lies exactly on the drawn ground.
    /// </summary>
    public static float HeightAt(float[] heights, float x, float z, GroundLayout? layout = null)
    {
        layout ??= GroundLayout.Quarter;
        int samples = layout.Samples;
        x = Math.Clamp(x, 0, layout.SampleAt(samples - 1));
        z = Math.Clamp(z, 0, layout.SampleAt(samples - 1));
        int x0 = Math.Min((int)x, samples - 2), z0 = Math.Min((int)z, samples - 2);
        // (In the quarter layout the last square is half a metre across.)
        float fx = (x - x0) / (layout.SampleAt(x0 + 1) - x0), fz = (z - z0) / (layout.SampleAt(z0 + 1) - z0);
        float h00 = heights[z0 * samples + x0], h10 = heights[z0 * samples + x0 + 1], h01 = heights[(z0 + 1) * samples + x0], h11 = heights[(z0 + 1) * samples + x0 + 1];
        return fx + fz <= 1
            ? h00 + fx * (h10 - h00) + fz * (h01 - h00)
            : h11 + (1 - fx) * (h01 - h11) + (1 - fz) * (h10 - h11);
    }

    /// <summary>
    /// The zone's nine sub-zones made from the samples (see <see cref="Sample"/>), each written over
    /// the zone's own (<paramref name="templates"/>, by number, which must have ground; see
    /// <see cref="TerrainSubzone.Write"/>), or from nothing if there are none, for the zone whose square starts at
    /// <paramref name="zoneCorner"/> (x, z in Armageddon's world).
    /// </summary>
    public static IReadOnlyList<(byte[] Header, byte[] Data)> Subzones(float[] heights, Vector2 zoneCorner, IReadOnlyList<(byte[] Header, byte[] Data)> templates) =>
        Subzones(heights, zoneCorner, templates, null);

    /// <summary>
    /// As <see cref="Subzones(float[], Vector2, IReadOnlyList{ValueTuple{byte[], byte[]}})"/>, and with
    /// <paramref name="cliffs"/> (see <see cref="Cliffs"/>), each patch's cliffs drawn where Guerrilla
    /// draws them, with textures projected from the side (cliff kinds 3-5, as the game's own
    /// sub-zones draw theirs), over the ground in a ring where Guerrilla's overlap, fading in there by
    /// each vertex's cliff alpha; the ground seen from above everywhere Guerrilla draws ground, and
    /// where nothing of its covers (the holes its rocks close); <paramref name="cliffMaterials"/>: how many
    /// cliff materials the zone's terrain file has. With <paramref name="holes"/> (see <see cref="UnderRoads"/>)
    /// the triangles under the roads are left out, and <paramref name="roads"/> holds each sub-zone's
    /// road decals to lay in them (only for sub-zones written from nothing).
    /// </summary>
    public static IReadOnlyList<(byte[] Header, byte[] Data)> Subzones(float[] heights, Vector2 zoneCorner, IReadOnlyList<(byte[] Header, byte[] Data)>? templates, CliffCover? cliffs,
                                                                     GroundLayout? layout = null, SquareCut[]? holes = null,
                                                                     IReadOnlyList<IReadOnlyList<RoadDecalToWrite>>? roads = null, int cliffMaterials = 3)
    {
        if (templates != null && templates.Count != 9) throw new ArgumentException("a zone has nine sub-zones");
        layout ??= GroundLayout.Quarter;
        int samples = layout.Samples;
        float step = layout.GridStep;
        var written = new List<(byte[], byte[])>();
        for (int n = 0; n < 9; n++)
        {
            // This third of the square both ways, in samples, sharing its edge samples with its neighbours.
            int x0 = n % 3 * (samples - 1) / 3, x1 = (n % 3 + 1) * (samples - 1) / 3;
            int z0 = n / 3 * (samples - 1) / 3, z1 = (n / 3 + 1) * (samples - 1) / 3;
            int width = x1 - x0 + 1;
            var vertices = new List<GroundVertex>();
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    // The slope from the neighbouring samples.
                    int xl = Math.Max(0, x - 1), xr = Math.Min(samples - 1, x + 1), zd = Math.Max(0, z - 1), zu = Math.Min(samples - 1, z + 1);
                    float left = heights[z * samples + xl], right = heights[z * samples + xr];
                    float down = heights[zd * samples + x], up = heights[zu * samples + x];
                    float spanX = (layout.StepOf(xr) - layout.StepOf(xl)) * step, spanZ = (layout.StepOf(zu) - layout.StepOf(zd)) * step;
                    var normal = Vector3.Normalize(new Vector3(-(right - left) / spanX, 1, -(up - down) / spanZ));
                    vertices.Add(new GroundVertex(layout.StepOf(x) - layout.StepOf(x0), layout.StepOf(z) - layout.StepOf(z0), heights[z * samples + x], normal,
                                                  cliffs?.AlphaAt(z * samples + x) ?? 0xFF));
                }
            // 5 × 5 patches, each a strip along x, a row of squares at a time (rows joined by repeated indices).
            var patches = new List<IReadOnlyList<TerrainSubzone.PatchBlock>>();
            int quadsX = x1 - x0, quadsZ = z1 - z0;
            for (int pz = 0; pz < PatchesAcross; pz++)
                for (int px = 0; px < PatchesAcross; px++)
                {
                    int qx0 = px * quadsX / PatchesAcross, qx1 = (px + 1) * quadsX / PatchesAcross;
                    int qz0 = pz * quadsZ / PatchesAcross, qz1 = (pz + 1) * quadsZ / PatchesAcross;
                    var strip = new List<int>();
                    for (int qz = qz0; qz < qz1; qz++)
                    {
                        if (strip.Count > 0)
                        {
                            strip.Add(strip[^1]);
                            strip.Add(qz * width + qx0);
                        }
                        // a: this row's sample, b: the next row's (higher z): triangles (a0, b0, a1), (a1, b0, b1), ... face up.
                        for (int qx = qx0; qx <= qx1; qx++)
                        {
                            strip.Add(qz * width + qx);
                            strip.Add((qz + 1) * width + qx);
                        }
                    }
                    IReadOnlyList<TerrainSubzone.PatchBlock> blocks = cliffs == null && holes == null
                        ? [new TerrainSubzone.PatchBlock(TerrainSubzone.GroundKind, [.. strip])]
                        : CliffBlocks(vertices, width, qx0, qx1, qz0, qz1, x0, z0, cliffs, holes, layout, cliffMaterials);
                    if (blocks.Count > 0) patches.Add(blocks);  // (none if a road covers it all)
                }
            var origin = new Vector2(zoneCorner.X + layout.StepOf(x0) * step, zoneCorner.Y + layout.StepOf(z0) * step);
            written.Add(templates != null ? TerrainSubzone.Write(templates[n].Header, templates[n].Data, origin, vertices, patches, step)
                                          : TerrainSubzone.Write(n, origin, vertices, patches, step, roads?[n]));
        }
        return written;
    }

    /// <summary>
    /// Where Guerrilla's sub-zones draw their cliffs, over a square of ground samples (see
    /// <see cref="Cliffs"/>). For each square of samples, four points: a third and two thirds across
    /// it, the middles of its two triangles cut either way (0 and 1 the usual cut's first and second,
    /// 2 and 3 the other cut's, as <see cref="SquareCut"/> numbers them); each says whether Guerrilla's
    /// ground draws there (<see cref="Ground"/>) and which of its cliffs (the render block's cliff
    /// kind, 3 to 5; 0 none). And at each sample, how much of a cliff shows there (Guerrilla's vertex
    /// there: 0x7F none, 0xFF all).
    /// </summary>
    public sealed class CliffCover(byte[] points, byte[] alpha)
    {
        public const byte Ground = 0x80, KindMask = 0x7F;
        public byte At(int square, int point) => points[square * 4 + point];
        public byte AlphaAt(int sample) => alpha[sample];
    }

    /// <summary>
    /// Guerrilla's cliffs over the square from <paramref name="corner"/>, from its sub-zones there.
    /// Guerrilla draws a patch's ground and its cliffs (textures projected from the side) as render
    /// blocks of their own that overlap in a ring round each cliff: there both draw, and the cliff
    /// fades in over the ground by its vertices' alpha (0x7F at the ring's outer edge, 0xFF inside;
    /// the start zone's cliff sub-zone terr01_12_11_7: 7,814 cliff triangles, 3,974 of them the ground's
    /// too). Its ground is on the same 1 m grid as ours but cuts its squares the other way, so each of
    /// our triangles takes what covers its middle. A cliff block's kind is 3 + the number of the zone's
    /// cliff material it draws with (Guerrilla's zones have up to six: kinds 3-8), kept as Guerrilla's.
    /// </summary>
    public static CliffCover Cliffs(IEnumerable<TerrainSubzone> subzones, Vector2 corner, GroundLayout? layout = null)
    {
        layout ??= GroundLayout.Quarter;
        int samples = layout.Samples, squares = samples - 1;
        var points = new byte[squares * squares * 4];
        var alpha = new byte[samples * samples];
        var seen = new bool[samples * samples];
        (float X, float Z)[] middles = [(1 / 3f, 1 / 3f), (2 / 3f, 2 / 3f), (2 / 3f, 1 / 3f), (1 / 3f, 2 / 3f)];
        int Square(float metres) => Math.Clamp((int)MathF.Floor(metres), 0, squares - 1);
        float Across(int square, float part) => layout.SampleAt(square) + part * (layout.SampleAt(square + 1) - layout.SampleAt(square));
        foreach (var subzone in subzones)
        {
            for (int v = 0; v < subzone.Positions.Length; v++)
            {
                float x = subzone.Positions[v].X - corner.X, z = subzone.Positions[v].Z - corner.Y;
                int sx = (int)MathF.Round(x), sz = (int)MathF.Round(z);
                if (sx < 0 || sz < 0 || sx >= samples || sz >= samples || MathF.Abs(layout.SampleAt(sx) - x) > 0.25f || MathF.Abs(layout.SampleAt(sz) - z) > 0.25f) continue;
                int at = sz * samples + sx;
                alpha[at] = Math.Max(alpha[at], subzone.CliffAlpha[v]);
                seen[at] = true;
            }
            for (int t = 0; t < subzone.TriangleKinds.Length; t++)
            {
                Vector3 a = subzone.Positions[subzone.Triangles[3 * t]], b = subzone.Positions[subzone.Triangles[3 * t + 1]], c = subzone.Positions[subzone.Triangles[3 * t + 2]];
                var p0 = new Vector2(a.X - corner.X, a.Z - corner.Y);
                var p1 = new Vector2(b.X - corner.X, b.Z - corner.Y);
                var p2 = new Vector2(c.X - corner.X, c.Z - corner.Y);
                float area = Cross(p1 - p0, p2 - p0);
                if (MathF.Abs(area) < 1e-6f) continue;
                if (MathF.Max(p0.X, MathF.Max(p1.X, p2.X)) < 0 || MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y)) < 0 ||
                    MathF.Min(p0.X, MathF.Min(p1.X, p2.X)) > squares + 1 || MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y)) > squares + 1) continue;
                uint kind = subzone.TriangleKinds[t];
                byte mark = kind == TerrainSubzone.GroundKind ? Ground : (byte)Math.Min(kind, KindMaskOf);
                if (kind != TerrainSubzone.GroundKind && kind < TerrainSubzone.CliffKindLow) continue;  // (no such blocks in Guerrilla's)
                int fromX = Square(MathF.Min(p0.X, MathF.Min(p1.X, p2.X))), toX = Square(MathF.Max(p0.X, MathF.Max(p1.X, p2.X)));
                int fromZ = Square(MathF.Min(p0.Y, MathF.Min(p1.Y, p2.Y))), toZ = Square(MathF.Max(p0.Y, MathF.Max(p1.Y, p2.Y)));
                for (int qz = fromZ; qz <= toZ; qz++)
                    for (int qx = fromX; qx <= toX; qx++)
                        for (int m = 0; m < 4; m++)
                        {
                            var p = new Vector2(Across(qx, middles[m].X), Across(qz, middles[m].Z));
                            // Inside (edges included): the same side of all three edges as the triangle's area.
                            float w0 = Cross(p1 - p0, p - p0) / area, w1 = Cross(p2 - p1, p - p1) / area, w2 = Cross(p0 - p2, p - p2) / area;
                            if (w0 < -1e-4f || w1 < -1e-4f || w2 < -1e-4f) continue;
                            ref byte point = ref points[((qz * squares) + qx) * 4 + m];
                            if (mark == Ground) point |= Ground;
                            else if ((point & KindMaskOf) == 0) point |= mark;
                        }
            }
        }
        for (int s = 0; s < alpha.Length; s++)
            if (!seen[s]) alpha[s] = 0xFF;
        return new CliffCover(points, alpha);
    }

    const byte Ground = CliffCover.Ground, KindMaskOf = CliffCover.KindMask;

    static float Cross(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;

    /// <summary>
    /// One patch (squares qx0–qx1, qz0–qz1 of a sub-zone whose first sample is x0, z0; its
    /// vertices <paramref name="width"/> to a row) as render blocks: the ground's triangles, then
    /// each cliff kind's (see <see cref="Cliffs"/>: a triangle in Guerrilla's ring is in both), every
    /// square cut as the strips cut it (from its corner at (x, z + 1) to the one at (x + 1, z)) but
    /// where <paramref name="holes"/> say otherwise, and without the triangles they leave out. Each
    /// block is a strip made of the rows' strips: a row runs a0, b0, a1, b1, ... (its triangle 2k is
    /// square k's first, 2k + 1 its second), and each run of a row's triangles in one block is a piece
    /// of it, so a block takes about two indices a triangle, as the game's own; a square cut the other
    /// way is a piece of its own for each of its triangles.
    /// </summary>
    static IReadOnlyList<TerrainSubzone.PatchBlock> CliffBlocks(List<GroundVertex> vertices, int width, int qx0, int qx1, int qz0, int qz1,
                                                                 int x0, int z0, CliffCover? cliffs, SquareCut[]? holes, GroundLayout layout, int cliffMaterials)
    {
        // The highest cliff kind the zone's terrain file has a material for (a block naming one past it draws with that one).
        uint lastKind = TerrainSubzone.CliffKindLow + (uint)Math.Max(cliffMaterials, 1) - 1;
        int squaresAcross = layout.Samples - 1;
        // What draws a triangle (by the point in its middle): the ground (where Guerrilla's does, or
        // nothing of its covers) and a cliff kind (0 none). A cliff that isn't whole at all three corners
        // fades there, so the ground is drawn under it too: our squares are cut the other way from
        // Guerrilla's, and a few of our cliff-only triangles touch the edge of its ring (alpha 0x7F),
        // where without the ground they faded to nothing (dark).
        (bool Ground, uint Cliff) Cover(int qx, int qz, int point, int a, int b, int c)
        {
            if (cliffs == null) return (true, 0);
            byte at = cliffs.At((z0 + qz) * squaresAcross + x0 + qx, point);
            uint kind = (uint)(at & CliffCover.KindMask);
            bool fades = Math.Min(vertices[a].CliffAlpha, Math.Min(vertices[b].CliffAlpha, vertices[c].CliffAlpha)) < 0xFF;
            return (kind == 0 || (at & CliffCover.Ground) != 0 || fades, Math.Min(kind, lastKind));
        }
        // Each kind's pieces of strip, and whether a piece's first triangle must be at an odd place
        // (where a strip turns its triangle round).
        var pieces = new Dictionary<uint, List<(int[] Vertices, bool Odd)>>();
        void Piece(uint kind, int[] piece, bool odd)
        {
            if (!pieces.TryGetValue(kind, out var list)) pieces[kind] = list = [];
            list.Add((piece, odd));
        }
        int squares = qx1 - qx0;
        var groundAt = new bool[2 * squares];
        var cliffAt = new uint[2 * squares];
        for (int qz = qz0; qz < qz1; qz++)
        {
            Array.Clear(groundAt);
            Array.Clear(cliffAt);
            for (int k = 0; k < squares; k++)
            {
                int a0 = qz * width + qx0 + k, a1 = a0 + 1, b0 = a0 + width, b1 = b0 + 1;
                SquareCut cut = holes?[(z0 + qz) * (layout.Samples - 1) + x0 + qx0 + k] ?? SquareCut.None;
                if (cut.HasFlag(SquareCut.Flipped))
                {
                    if (!cut.HasFlag(SquareCut.First))
                    {
                        var (ground, cliff) = Cover(qx0 + k, qz, 2, a0, b1, a1);
                        if (ground) Piece(TerrainSubzone.GroundKind, [a0, b1, a1], false);
                        if (cliff != 0) Piece(cliff, [a0, b1, a1], false);
                    }
                    if (!cut.HasFlag(SquareCut.Second))
                    {
                        var (ground, cliff) = Cover(qx0 + k, qz, 3, a0, b0, b1);
                        if (ground) Piece(TerrainSubzone.GroundKind, [a0, b0, b1], false);
                        if (cliff != 0) Piece(cliff, [a0, b0, b1], false);
                    }
                    continue;
                }
                if (!cut.HasFlag(SquareCut.First)) (groundAt[2 * k], cliffAt[2 * k]) = Cover(qx0 + k, qz, 0, a0, b0, a1);
                if (!cut.HasFlag(SquareCut.Second)) (groundAt[2 * k + 1], cliffAt[2 * k + 1]) = Cover(qx0 + k, qz, 1, a1, b0, b1);
            }
            int RowVertex(int i) => (i % 2 == 0 ? qz : qz + 1) * width + qx0 + i / 2;
            // Runs of the row's triangles in the ground's block, then in each cliff kind's.
            for (int t = 0; t < groundAt.Length;)
            {
                if (!groundAt[t])
                {
                    t++;
                    continue;
                }
                int end = t;
                while (end + 1 < groundAt.Length && groundAt[end + 1]) end++;
                Piece(TerrainSubzone.GroundKind, [.. Enumerable.Range(t, end - t + 3).Select(RowVertex)], t % 2 == 1);
                t = end + 1;
            }
            for (int t = 0; t < cliffAt.Length;)
            {
                uint kind = cliffAt[t];
                if (kind == 0)
                {
                    t++;
                    continue;
                }
                int end = t;
                while (end + 1 < cliffAt.Length && cliffAt[end + 1] == kind) end++;
                Piece(kind, [.. Enumerable.Range(t, end - t + 3).Select(RowVertex)], t % 2 == 1);
                t = end + 1;
            }
        }
        return pieces.OrderBy(k => k.Key).Select(k => new TerrainSubzone.PatchBlock(k.Key, Joined(k.Value))).ToList();
    }

    /// <summary>
    /// Pieces of strip joined into one strip by repeated indices (which make no triangles), each
    /// piece's first triangle at an even place, or an odd one where it says so.
    /// </summary>
    static int[] Joined(List<(int[] Vertices, bool Odd)> pieces)
    {
        var strip = new List<int>();
        foreach (var (piece, odd) in pieces)
        {
            if (strip.Count > 0)
            {
                strip.Add(strip[^1]);
                strip.Add(piece[0]);
            }
            if (strip.Count % 2 != (odd ? 1 : 0)) strip.Add(piece[0]);
            strip.AddRange(piece);
        }
        return [.. strip];
    }
}
