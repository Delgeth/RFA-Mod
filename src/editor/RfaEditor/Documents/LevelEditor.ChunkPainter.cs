using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using RfaFormats.Zones;

namespace RfaEditor;

// The chunk painter: two of the sculptor's modes (Paint chunks, Erase chunks). Hold the left button
// and chunks (props, rocks, crates, debris...) are scattered over the ground under the brush, each a
// whole object as the Add list makes it (a copy of where the game's levels place it, its files
// brought in on saving), turned at random and set on the ground where it lands, the ground's height
// there (its box's bottom a little into the ground, so it sits and doesn't float), no nearer to
// another chunk than the style's spacing. Shift erases instead: the chunks of the style under the
// brush. A stroke is one change to undo.
//
// The styles: presets made from the game's chunks by what their names say (rocks, containers,
// debris, fences, lights; colonist, EDF, Marauder, ruins; the Guerrilla world's props), and the
// layout of any zone of the game's or the Guerrilla world's: the chunks its objects use, as often
// as it uses them, and as far apart as they stand there (the middle of their distances to the
// nearest other), leaving out buildings (anything over 15 m across).
public partial class LevelEditor
{
    sealed record ChunkStyle(string Name, IReadOnlyList<(string Chunk, float Weight)> Chunks, float Spacing);

    List<string>? chunkStyleNames;
    readonly Dictionary<string, ChunkStyle?> chunkStyles = new(StringComparer.OrdinalIgnoreCase);
    readonly List<ZoneObject> chunkStrokeAdded = [];
    readonly List<RemovedObjects> chunkStrokeRemoved = [];
    readonly List<string> chunkStrokeNeeds = [];
    bool chunkStroke;
    float chunkCarry;
    readonly Random chunkRandom = new();
    const string ZoneStylePrefix = "Like zone: ";

    static readonly (string Name, float Spacing, Func<ChunkEntry, bool> Takes)[] ChunkPresets =
    [
        ("Rocks and boulders", 7, e => e.Category == "Rocks" || e.Type == "Terrain pieces"),
        ("Crates and containers", 3, e => e.Category == "Containers"),
        ("Trash and debris", 2.5f, e => e.Category == "Trash and debris"),
        ("Fences and barricades", 5, e => e.Category is "Fences" or "Barricades"),
        ("Industrial clutter", 4, e => e.Category is "Industrial" or "Pipes" or "Vents" or "Technology"),
        ("Lights", 10, e => e.Category == "Lights"),
        ("Colonist settlement", 5, e => e.Setting == "Colonist" && e.Type is "Props" or "Other"),
        ("EDF outpost", 5, e => e.Setting == "EDF" && e.Type is "Props" or "Other"),
        ("Marauder camp", 5, e => e.Setting == "Marauder"),
        ("Ruins", 5, e => e.Setting == "Ruins"),
        ("Guerrilla world props", 4, e => e.Places.Any(p => p.Pack.StartsWith("rfg", StringComparison.OrdinalIgnoreCase)) && e.Type != "Structures" && e.Category != "Buildings"),
    ];

    /// <summary>The style box: the presets, then every zone (the game's and the Guerrilla world's) to take a layout from.</summary>
    void EnsureChunkStyles()
    {
        if (chunkStyleNames != null || !context.Assets.IndexReady) return;
        var zones = context.Assets.NamesWhere(n => n.EndsWith(".rfgzone_pc", StringComparison.OrdinalIgnoreCase) && !n.StartsWith("p_", StringComparison.OrdinalIgnoreCase))
                                  .Select(n => n[..^".rfgzone_pc".Length]).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        chunkStyleNames = [.. ChunkPresets.Select(p => p.Name), .. zones.Select(z => ZoneStylePrefix + z)];
        ChunkStyleBox.ItemsSource = chunkStyleNames;
        ChunkStyleBox.SelectedIndex = 0;
        _ = context.Assets.ChunkPlacements();  // (where the game's levels place each chunk: looked up once, now, not in the first stroke)
    }

    string ChosenChunkStyle => (ChunkStyleBox.Text ?? "").Trim() is { Length: > 0 } text ? text : chunkStyleNames?.FirstOrDefault() ?? "";

    /// <summary>A style by name (made once); null, said in the log, if it has no chunks.</summary>
    ChunkStyle? StyleNamed(string name)
    {
        if (chunkStyles.TryGetValue(name, out ChunkStyle? known)) return known;
        ChunkStyle? style = null;
        if (name.StartsWith(ZoneStylePrefix, StringComparison.OrdinalIgnoreCase)) style = ZoneStyle(name, name[ZoneStylePrefix.Length..].Trim());
        else
            foreach (var preset in ChunkPresets)
            {
                if (!preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                var chunks = context.Assets.ChunkCatalog().Where(e => !e.Stitch && preset.Takes(e)).Select(e => (e.Name, 1f)).ToList();
                style = chunks.Count > 0 ? new ChunkStyle(name, chunks, preset.Spacing) : null;
                break;
            }
        if (style == null) context.Log($"Chunk painter: the style {name} has no chunks to paint.");
        else context.Log($"Chunk painter: {name}: {style.Chunks.Count} chunks, about {style.Spacing:0.#} m apart. Hold the left button to paint (Shift: erase them).");
        chunkStyles[name] = style;
        return style;
    }

    /// <summary>A zone's layout as a style (see the top).</summary>
    ChunkStyle? ZoneStyle(string name, string zoneName)
    {
        if (context.Assets.ReadAnywhere(zoneName + ".rfgzone_pc") is not { } bytes) return null;
        ZoneFile source;
        try { source = ZoneFile.Read(bytes); }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or NotSupportedException) { return null; }
        var things = source.Objects.Where(o => o.Parent == ZoneObject.None && o.Property("chunk_name")?.Text is string c && c.EndsWith(".cchk_pc", StringComparison.OrdinalIgnoreCase))
                                   .Where(o => { Vector3 size = o.BoundsMax - o.BoundsMin; return MathF.Max(size.X, MathF.Max(size.Y, size.Z)) <= 15; })
                                   .ToList();
        if (things.Count == 0) return null;
        var counts = things.GroupBy(o => System.IO.Path.GetFileNameWithoutExtension(o.Property("chunk_name")!.Text!), StringComparer.OrdinalIgnoreCase)
                           .Select(g => (g.Key, (float)g.Count())).ToList();
        // How far apart they stand: the middle of each one's distance to its nearest other.
        var nearest = new List<float>();
        foreach (ZoneObject a in things.Take(600))
        {
            float best = float.MaxValue;
            foreach (ZoneObject b in things)
                if (!ReferenceEquals(a, b)) best = MathF.Min(best, Vector3.Distance(a.Placement.Position, b.Placement.Position));
            if (best < float.MaxValue) nearest.Add(best);
        }
        nearest.Sort();
        float spacing = nearest.Count > 0 ? Math.Clamp(nearest[nearest.Count / 2], 1.5f, 40) : 5;
        return new ChunkStyle(name, counts, spacing);
    }

    bool BeginChunkStroke()
    {
        EnsureChunkStyles();
        if (StyleNamed(ChosenChunkStyle) == null) return false;
        if (ChunkContainer() == null)
        {
            context.Log($"Chunk painter: {zoneName} isn't inside a container, so there is nowhere to put the chunks' models.");
            return false;
        }
        chunkStroke = true;
        chunkCarry = 0;
        chunkStrokeAdded.Clear();
        chunkStrokeRemoved.Clear();
        chunkStrokeNeeds.Clear();
        return true;
    }

    /// <summary>A step of a chunk stroke: chunks set down under the brush where there's room (or the style's taken away).</summary>
    void ChunkStrokeStep(Vector2 centre, float radius, float amount, bool erase)
    {
        if (!chunkStroke || StyleNamed(ChosenChunkStyle) is not { } style) return;
        var styleChunks = style.Chunks.Select(c => c.Chunk).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? ChunkOf(ZoneObject o) => o.Property("chunk_name")?.Text is string c && c.EndsWith(".cchk_pc", StringComparison.OrdinalIgnoreCase)
            ? System.IO.Path.GetFileNameWithoutExtension(c) : null;
        if (erase)
        {
            var going = zone.Objects.Where(o => o.Parent == ZoneObject.None && ChunkOf(o) is string c && styleChunks.Contains(c) &&
                                                Vector2.Distance(new Vector2(o.Placement.Position.X, o.Placement.Position.Z), centre) <= radius)
                                    .Take(4).ToList();
            foreach (ZoneObject root in going)
            {
                var group = zone.WithAttached(root).ToList();
                chunkStrokeRemoved.Add(zone.Remove(group));
                foreach (ZoneObject o in group) scene?.Remove(o);
            }
            if (going.Count > 0) ObjectsChanged();
            return;
        }
        // How many to try this frame: the strength sets how fast the brush fills.
        chunkCarry += amount * 0.6f;
        int tries = Math.Min(4, (int)chunkCarry);
        chunkCarry -= tries;
        float total = style.Chunks.Sum(c => c.Weight);
        var (targetArchive, targetContainer) = ChunkContainer()!.Value;
        for (int t = 0; t < tries; t++)
        {
            float angle = (float)(chunkRandom.NextDouble() * Math.PI * 2), distance = radius * MathF.Sqrt((float)chunkRandom.NextDouble());
            var spot = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
            if (sculptZones.Count > 0 && !sculptZones.Any(z => z.Covers(spot.X, spot.Y))) continue;
            // Room: no chunk nearer than the spacing.
            bool crowded = zone.Objects.Any(o => o.Parent == ZoneObject.None && ChunkOf(o) != null &&
                                                 Vector2.Distance(new Vector2(o.Placement.Position.X, o.Placement.Position.Z), spot) < style.Spacing);
            if (crowded) continue;
            // Which: by how often the style uses each.
            float pick = (float)chunkRandom.NextDouble() * total;
            string chunk = style.Chunks[^1].Chunk;
            foreach (var (name, weight) in style.Chunks)
            {
                if ((pick -= weight) <= 0) { chunk = name; break; }
            }
            float ground = GroundHeight(spot.X, spot.Y);
            IReadOnlyList<ZoneObject> made;
            try { made = MakeChunkObjects(chunk, new Vector3(spot.X, ground, spot.Y), targetContainer, chunkStrokeNeeds, out _); }
            catch (InvalidOperationException ex)
            {
                context.Log($"Chunk painter: {chunk} couldn't be added ({ex.Message}).");
                continue;
            }
            if (made.Count == 0) continue;
            SetOnGround(made, (float)(chunkRandom.NextDouble() * Math.PI * 2));
            foreach (ZoneObject o in made) scene?.Add(o);
            chunkStrokeAdded.AddRange(made);
        }
        if (tries > 0) ObjectsChanged();
    }

    /// <summary>
    /// Chunks just made (their first the root): turned about it by <paramref name="yaw"/> (all of them,
    /// so pieces joined to it stay joined), then moved up or down so the root's box's bottom is a
    /// little into the ground under it (a tenth of its height, at most half a metre).
    /// </summary>
    void SetOnGround(IReadOnlyList<ZoneObject> made, float yaw)
    {
        ZoneObject root = made[0];
        Vector3 pivot = root.Placement.Position;
        Matrix4x4 turn = Matrix4x4.CreateRotationY(yaw);
        foreach (ZoneObject o in made)
        {
            if (!o.CanTurn) continue;
            (Vector3 p, Matrix4x4 r) = o.Placement;
            o.TurnTo(r * turn);
            o.MoveTo(pivot + Vector3.Transform(p - pivot, turn));
        }
        float height = root.BoundsMax.Y - root.BoundsMin.Y;
        float sink = Math.Clamp(height * 0.1f, 0.02f, 0.5f);
        float up = GroundHeight(pivot.X, pivot.Z) - sink - root.BoundsMin.Y;
        foreach (ZoneObject o in made) o.MoveTo(o.Placement.Position + new Vector3(0, up, 0));
    }

    /// <summary>Ends a chunk stroke (if one was going on): one change to undo. True if it was one.</summary>
    bool EndChunkStroke()
    {
        if (!chunkStroke) return false;
        chunkStroke = false;
        var added = chunkStrokeAdded.ToList();
        var removed = chunkStrokeRemoved.ToList();
        chunkStrokeAdded.Clear();
        chunkStrokeRemoved.Clear();
        if (added.Count == 0 && removed.Count == 0) return true;
        RemovedObjects? addedTaken = null;
        void Undo()
        {
            if (added.Count > 0) { addedTaken = zone.Remove(added); foreach (ZoneObject o in added) scene?.Remove(o); }
            for (int i = removed.Count - 1; i >= 0; i--) { zone.PutBack(removed[i]); foreach (var (o, _) in removed[i].Objects) scene?.Add(o); }
        }
        void Redo()
        {
            foreach (RemovedObjects r in removed) { var objects = r.Objects.Select(x => x.Object).ToList(); zone.Remove(objects); foreach (ZoneObject o in objects) scene?.Remove(o); }
            if (addedTaken != null) { zone.PutBack(addedTaken); foreach (ZoneObject o in added) scene?.Add(o); }
        }
        int roots = added.Count(o => o.Parent == ZoneObject.None);
        Record(new Change(added.FirstOrDefault(), $"Paint chunks ({roots} added, {removed.Count} erased)", Undo: () => { Undo(); ObjectsChanged(); },
                          Redo: () => { Redo(); ObjectsChanged(); }, AddsOrRemoves: true));
        if (chunkStrokeNeeds.Count > 0)
            context.Log($"Chunk painter: brought in on saving: {string.Join(", ", chunkStrokeNeeds.Distinct(StringComparer.OrdinalIgnoreCase))}.");
        chunkStrokeNeeds.Clear();
        return true;
    }
}
