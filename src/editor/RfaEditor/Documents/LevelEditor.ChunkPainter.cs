using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using RfaFormats.Zones;

namespace RfaEditor;

// The chunk painter: two of the sculptor's modes (Paint chunks, Erase chunks). Hold the left button
// and real layouts are set down on the ground under the brush: groups of chunks as a level of the
// game's (or the Guerrilla world's) has them, copied whole (every object of the group where it
// stands in it, turned as it is there, with the pieces and things attached to it, its files brought
// in on saving), the group turned as one at random, following the ground: each object moved up or
// down with the ground under it, and the group set so its lowest objects sit on the ground. Groups
// keep apart by the gap the source keeps between its groups. Shift erases instead: the style's
// chunks under the brush. A stroke is one change to undo.
//
// A group: the chunks of a level that stand together, each within a few metres of another (twice
// the usual distance there between a chunk and its nearest, 3 to 14 m), up to 40 objects. The
// styles: any zone's own groups ("Like zone: ..."), or a preset (rocks, containers, debris, fences,
// lights; colonist, EDF, Marauder, ruins; the Guerrilla world's props): the groups of the levels
// that place those chunks, mostly made of them. A preset with no such groups scatters its chunks
// one by one.
public partial class LevelEditor
{
    /// <summary>A layout taken from a level: its objects (the topmost of each, with those joined or attached to it), their middle, and how far they reach from it.</summary>
    sealed record ChunkGroup(ZoneFile Source, IReadOnlyList<IReadOnlyList<ZoneObject>> Members, Vector3 Middle, float Reach, string Where);

    sealed record ChunkStyle(string Name, IReadOnlyList<ChunkGroup> Groups, IReadOnlyList<string> Singles, float Gap, HashSet<string> Chunks);

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

    static string? ChunkNameOf(ZoneObject o) => o.Property("chunk_name")?.Text is string c && c.EndsWith(".cchk_pc", StringComparison.OrdinalIgnoreCase)
        ? System.IO.Path.GetFileNameWithoutExtension(c) : null;

    /// <summary>A style by name (made once); null, said in the log, if it has nothing to paint.</summary>
    ChunkStyle? StyleNamed(string name)
    {
        if (chunkStyles.TryGetValue(name, out ChunkStyle? known)) return known;
        ChunkStyle? style = null;
        if (name.StartsWith(ZoneStylePrefix, StringComparison.OrdinalIgnoreCase))
        {
            string zoneName = name[ZoneStylePrefix.Length..].Trim();
            if (ReadSourceZone(null, null, zoneName + ".rfgzone_pc") is { } source)
            {
                var groups = GroupsIn(source, zoneName, _ => true, out float gap);
                if (groups.Count > 0) style = new ChunkStyle(name, groups, [], gap, ChunksOf(groups));
            }
        }
        else
            foreach (var preset in ChunkPresets)
            {
                if (!preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                var chunks = context.Assets.ChunkCatalog().Where(e => !e.Stitch && preset.Takes(e)).Select(e => e.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                if (chunks.Count == 0) break;
                // The levels that place these chunks: their groups made mostly of them.
                var placements = context.Assets.ChunkPlacements();
                if (!placements.IsCompleted)
                {
                    context.Log("Chunk painter: looking through the game's levels for where they place each chunk (once)...");
                    placements.Wait(TimeSpan.FromSeconds(60));
                }
                var groups = new List<ChunkGroup>();
                var gaps = new List<float>();
                if (placements.IsCompletedSuccessfully)
                {
                    var levels = chunks.Select(c => placements.Result.GetValueOrDefault(c)).OfType<ChunkPlacement>()
                                       .GroupBy(p => (p.Archive, p.Container, p.Zone)).OrderByDescending(g => g.Count()).Take(12);
                    foreach (var level in levels)
                    {
                        if (ReadSourceZone(level.Key.Archive, level.Key.Container, level.Key.Zone) is not { } source) continue;
                        var found = GroupsIn(source, System.IO.Path.GetFileNameWithoutExtension(level.Key.Zone),
                                             g => g.Count(o => ChunkNameOf(o) is string c && chunks.Contains(c)) >= Math.Max(1, (int)MathF.Ceiling(g.Count * 0.6f)), out float gap);
                        groups.AddRange(found);
                        if (found.Count > 0) gaps.Add(gap);
                    }
                }
                gaps.Sort();
                style = new ChunkStyle(name, groups, groups.Count > 0 ? new List<string>() : chunks.ToList(), gaps.Count > 0 ? gaps[gaps.Count / 2] : preset.Spacing,
                                       groups.Count > 0 ? ChunksOf(groups) : chunks);
                break;
            }
        if (style == null) context.Log($"Chunk painter: the style {name} has nothing to paint.");
        else if (style.Groups.Count > 0)
            context.Log($"Chunk painter: {name}: {style.Groups.Count} layouts from {string.Join(", ", style.Groups.Select(g => g.Where).Distinct().Take(6))}" +
                        $"{(style.Groups.Select(g => g.Where).Distinct().Count() > 6 ? "..." : "")}, {style.Groups.Sum(g => g.Members.Count)} chunks in all, kept about {style.Gap:0.#} m apart. " +
                        "Hold the left button to set them down (Shift: erase them).");
        else context.Log($"Chunk painter: {name}: no level has these chunks in groups of their own; its {style.Singles.Count} chunks are scattered one by one, about {style.Gap:0.#} m apart.");
        chunkStyles[name] = style;
        return style;
    }

    static HashSet<string> ChunksOf(IEnumerable<ChunkGroup> groups) =>
        groups.SelectMany(g => g.Members.SelectMany(m => m)).Select(ChunkNameOf).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>A zone file of the game's, from where it is (or wherever the index has it), read once.</summary>
    ZoneFile? ReadSourceZone(string? archive, string? containerName, string zoneFile)
    {
        string key = $"{archive}|{containerName}|{zoneFile}";
        if (chunkSourceZones.TryGetValue(key, out ZoneFile? known)) return known;
        try
        {
            byte[]? bytes;
            if (archive == null) bytes = context.Assets.ReadAnywhere(zoneFile);
            else
            {
                RfaFormats.Packfile pack = containerName == null ? Workspace.Archive(archive) : Workspace.Container(archive, containerName);
                bytes = pack.Find(zoneFile) is { } entry ? pack.Read(entry) : null;
            }
            if (bytes == null) return null;
            return chunkSourceZones[key] = ZoneFile.Read(bytes);
        }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or NotSupportedException or System.IO.FileNotFoundException) { return null; }
    }

    /// <summary>
    /// A level's groups (see the top): its chunks (the topmost objects naming a .cchk_pc, up to 30 m
    /// across), joined into groups by nearness, those <paramref name="keep"/> takes; and the gap it
    /// keeps between groups (the middle of the distances from each group to the nearest other).
    /// </summary>
    static List<ChunkGroup> GroupsIn(ZoneFile source, string where, Func<IReadOnlyList<ZoneObject>, bool> keep, out float gap)
    {
        gap = 6;
        var things = source.Objects.Where(o => o.Parent == ZoneObject.None && ChunkNameOf(o) != null)
                                   .Where(o => { Vector3 size = o.BoundsMax - o.BoundsMin; return MathF.Max(size.X, MathF.Max(size.Y, size.Z)) <= 30; })
                                   .ToList();
        if (things.Count == 0) return [];
        Vector3 P(ZoneObject o) => o.Placement.Position;
        // How near they usually stand: the middle of each one's distance to its nearest other.
        var nearest = new List<float>();
        foreach (ZoneObject a in things.Take(800))
        {
            float best = float.MaxValue;
            foreach (ZoneObject b in things)
                if (!ReferenceEquals(a, b)) best = MathF.Min(best, Vector3.Distance(P(a), P(b)));
            if (best < float.MaxValue) nearest.Add(best);
        }
        nearest.Sort();
        float link = nearest.Count > 0 ? Math.Clamp(nearest[nearest.Count / 2] * 2.2f, 3, 14) : 6;
        // Joined by nearness (each within the link of another).
        int[] root = Enumerable.Range(0, things.Count).ToArray();
        int Find(int i) { while (root[i] != i) i = root[i] = root[root[i]]; return i; }
        for (int i = 0; i < things.Count; i++)
            for (int j = i + 1; j < things.Count; j++)
                if (Vector3.DistanceSquared(P(things[i]), P(things[j])) <= link * link) root[Find(i)] = Find(j);
        var groups = new List<ChunkGroup>();
        foreach (var members in Enumerable.Range(0, things.Count).GroupBy(Find).Select(g => g.Select(i => things[i]).ToList()))
        {
            if (members.Count > 40 || !keep(members)) continue;
            Vector3 middle = members.Aggregate(Vector3.Zero, (sum, o) => sum + P(o)) / members.Count;
            float reach = members.Max(o => Vector2.Distance(new Vector2(P(o).X, P(o).Z), new Vector2(middle.X, middle.Z)) +
                                           MathF.Max(o.BoundsMax.X - o.BoundsMin.X, o.BoundsMax.Z - o.BoundsMin.Z) / 2);
            groups.Add(new ChunkGroup(source, members.Select(m => source.PiecesWith(m)).ToList(), middle, reach, where));
        }
        // The gap between groups: from each group's edge to the nearest other's.
        var gaps = new List<float>();
        foreach (ChunkGroup a in groups.Take(300))
        {
            float best = float.MaxValue;
            foreach (ChunkGroup b in groups)
                if (!ReferenceEquals(a, b))
                    best = MathF.Min(best, Vector2.Distance(new Vector2(a.Middle.X, a.Middle.Z), new Vector2(b.Middle.X, b.Middle.Z)) - a.Reach - b.Reach);
            if (best < float.MaxValue) gaps.Add(MathF.Max(1, best));
        }
        gaps.Sort();
        gap = gaps.Count > 0 ? Math.Clamp(gaps[gaps.Count / 2], 2, 40) : link;
        return groups;
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
        chunkCarry = 1;  // (one at once, where the stroke starts)
        chunkStrokeAdded.Clear();
        chunkStrokeRemoved.Clear();
        chunkStrokeNeeds.Clear();
        return true;
    }

    /// <summary>A step of a chunk stroke: layouts set down under the brush where there's room (or the style's chunks taken away).</summary>
    void ChunkStrokeStep(Vector2 centre, float radius, float amount, bool erase)
    {
        if (!chunkStroke || StyleNamed(ChosenChunkStyle) is not { } style) return;
        if (erase)
        {
            var going = zone.Objects.Where(o => o.Parent == ZoneObject.None && ChunkNameOf(o) is string c && style.Chunks.Contains(c) &&
                                                Vector2.Distance(new Vector2(o.Placement.Position.X, o.Placement.Position.Z), centre) <= radius)
                                    .Take(6).ToList();
            foreach (ZoneObject top in going)
            {
                var group = zone.WithAttached(top).ToList();
                chunkStrokeRemoved.Add(zone.Remove(group));
                foreach (ZoneObject o in group) scene?.Remove(o);
            }
            if (going.Count > 0) ObjectsChanged();
            return;
        }
        chunkCarry += amount * 0.25f;
        int tries = Math.Min(3, (int)chunkCarry);
        chunkCarry -= tries;
        var (_, targetContainer) = ChunkContainer()!.Value;
        bool any = false;
        for (int t = 0; t < tries; t++)
        {
            float angle = (float)(chunkRandom.NextDouble() * Math.PI * 2), distance = radius * MathF.Sqrt((float)chunkRandom.NextDouble());
            var spot = centre + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * distance;
            if (sculptZones.Count > 0 && !sculptZones.Any(z => z.Covers(spot.X, spot.Y))) continue;
            ChunkGroup? group = style.Groups.Count > 0 ? style.Groups[chunkRandom.Next(style.Groups.Count)] : null;
            float reach = group?.Reach ?? 1;
            // Room: no chunk within the group's reach and the gap.
            bool crowded = zone.Objects.Any(o => o.Parent == ZoneObject.None && ChunkNameOf(o) != null &&
                                                 Vector2.Distance(new Vector2(o.Placement.Position.X, o.Placement.Position.Z), spot) < reach + style.Gap);
            if (crowded) continue;
            float yaw = (float)(chunkRandom.NextDouble() * Math.PI * 2);
            try
            {
                var made = group != null ? StampGroup(group, spot, yaw, targetContainer)
                                         : MakeSingle(style.Singles[chunkRandom.Next(style.Singles.Count)], spot, yaw, targetContainer);
                foreach (ZoneObject o in made) scene?.Add(o);
                chunkStrokeAdded.AddRange(made);
                any |= made.Count > 0;
            }
            catch (InvalidOperationException ex)
            {
                context.Log($"Chunk painter: couldn't set a layout down ({ex.Message}).");
            }
        }
        if (any) ObjectsChanged();
    }

    List<ZoneObject> MakeSingle(string chunk, Vector2 spot, float yaw, string targetContainer)
    {
        var made = MakeChunkObjects(chunk, new Vector3(spot.X, GroundHeight(spot.X, spot.Y), spot.Y), targetContainer, chunkStrokeNeeds, out _).ToList();
        if (made.Count > 0) PlaceOnGround([made], new Vector3(spot.X, 0, spot.Y), yaw);
        return made;
    }

    /// <summary>A group copied whole (see the top), its middle at <paramref name="spot"/>, turned by <paramref name="yaw"/>, following the ground.</summary>
    List<ZoneObject> StampGroup(ChunkGroup group, Vector2 spot, float yaw, string targetContainer)
    {
        var at = new Vector3(spot.X, 0, spot.Y);
        var copies = new List<List<ZoneObject>>();
        foreach (IReadOnlyList<ZoneObject> member in group.Members)
            copies.Add([.. zone.AddCopiesFrom(group.Source, member, TakenHandles(), at - new Vector3(group.Middle.X, 0, group.Middle.Z))]);
        NoteChunksToBring(copies.SelectMany(c => c), targetContainer, chunkStrokeNeeds);
        PlaceOnGround(copies, at, yaw, group.Middle.Y);
        return [.. copies.SelectMany(c => c)];
    }

    /// <summary>
    /// Objects just made (each list one member: its topmost first, then what is joined or attached to
    /// it) turned about <paramref name="pivot"/> by <paramref name="yaw"/>, then following the ground:
    /// each member moved by how the ground under it differs from the ground under the pivot, and then
    /// all of them together so the lowest member's bottom is a little into the ground (a tenth of its
    /// height, at most half a metre): what stood on the ground in the level still does, and what stood
    /// on something stays on it.
    /// </summary>
    void PlaceOnGround(IReadOnlyList<List<ZoneObject>> members, Vector3 pivot, float yaw, float? sourceMiddleY = null)
    {
        Matrix4x4 turn = Matrix4x4.CreateRotationY(yaw);
        foreach (ZoneObject o in members.SelectMany(m => m))
        {
            (Vector3 p, Matrix4x4 r) = o.Placement;
            Vector3 flat = new(pivot.X, p.Y, pivot.Z);
            if (o.CanTurn) o.TurnTo(r * turn);
            o.MoveTo(flat + Vector3.Transform(p - flat, turn));
        }
        float groundMiddle = GroundHeight(pivot.X, pivot.Z);
        // Each member with the ground under it (from where the group's middle was, in its level, to here).
        float lowestGap = float.MaxValue;
        foreach (List<ZoneObject> member in members)
        {
            if (member.Count == 0) continue;
            ZoneObject top = member[0];
            Vector3 p = top.Placement.Position;
            float ground = GroundHeight(p.X, p.Z);
            float up = (ground - groundMiddle) + (groundMiddle - (sourceMiddleY ?? groundMiddle));
            foreach (ZoneObject o in member) o.MoveTo(o.Placement.Position + new Vector3(0, up, 0));
            float height = top.BoundsMax.Y - top.BoundsMin.Y;
            float sink = Math.Clamp(height * 0.1f, 0.02f, 0.5f);
            lowestGap = MathF.Min(lowestGap, top.BoundsMin.Y - (ground - sink));
        }
        if (lowestGap == float.MaxValue) return;
        foreach (ZoneObject o in members.SelectMany(m => m)) o.MoveTo(o.Placement.Position - new Vector3(0, lowestGap, 0));
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
        int tops = added.Count(o => o.Parent == ZoneObject.None);
        Record(new Change(added.FirstOrDefault(), $"Paint chunks ({tops} added, {removed.Count} erased)", Undo: () => { Undo(); ObjectsChanged(); },
                          Redo: () => { Redo(); ObjectsChanged(); }, AddsOrRemoves: true));
        if (chunkStrokeNeeds.Count > 0)
            context.Log($"Chunk painter: brought in on saving: {string.Join(", ", chunkStrokeNeeds.Distinct(StringComparer.OrdinalIgnoreCase))}.");
        chunkStrokeNeeds.Clear();
        return true;
    }
}
