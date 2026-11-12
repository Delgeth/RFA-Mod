using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RfaFormats;
using RfaFormats.Meshes;
using RfaFormats.Zones;
using Point = System.Windows.Point;

namespace RfaEditor;

// The level editor's terrain sculptor and its Xbox controller (XInput) controls.
public partial class LevelEditor
{
    // ---- Terrain sculptor ----------------------------------------------------------------------

    // The ground's models, by zone (its terrain files' name): one model a sub-zone, in the order of
    // their numbers, not frozen, so the sculptor can give them new meshes (this zone's, and the
    // zones around's: see BuildTerrain and BuildNeighbours).
    readonly Dictionary<string, (Model3DGroup Models, List<int> Numbers)> groundModels = new(StringComparer.OrdinalIgnoreCase);
    TerrainSculpt? sculpt;              // this zone's ground, held for sculpting (read when the tool is first chosen)
    List<TerrainSculpt> sculptZones = []; // ...and the zones around's, so strokes carry on over the edges (this zone's first)
    readonly ModelVisual3D brushVisual = new();
    Point? mouseBrush;
    Point? brushAt                      // where the mouse points the brush (the controller uses the middle of the view)
    {
        get => mouseBrush;
        set { mouseBrush = value; padSculpting = false; }
    }
    Dictionary<TerrainSculpt, float[]>? strokeBefore;  // each zone's ground as a stroke began, for undoing it
    Dictionary<ZoneObject, float>? standing;            // this zone's objects on the ground as a stroke began: how far above it
    readonly Dictionary<ZoneObject, byte[]> strokeObjects = [];  // the objects a stroke moved, as they were before it
    float flattenTo;
    DateTime lastSculpt;
    bool strokeFromPad, padInverse;
    float padStrength = 1;

    SculptMode ChosenMode => (SculptMode)Math.Clamp(SculptModeBox.SelectedIndex, 0, (int)SculptMode.PaintRoad);
    float BrushRadius => (float)SculptRadiusSlider.Value;
    bool SculptChanged => sculptZones.Any(z => z.Changed);

    /// <summary>A zone's name as its terrain files have it (p_ zones share their zone's).</summary>
    static string GroundKey(string zone) => zone.StartsWith("p_", StringComparison.OrdinalIgnoreCase) ? zone[2..] : zone;

    /// <summary>Notes a zone's ground models (just built), and shows them sculpted if they are.</summary>
    void NoteGround(string zone, Model3DGroup models, IEnumerable<int> numbers)
    {
        groundModels[GroundKey(zone)] = (models, numbers.ToList());
        if (sculptZones.FirstOrDefault(z => z.Zone.Equals(GroundKey(zone), StringComparison.OrdinalIgnoreCase)) is { } sculpted && sculpted.Changed)
            ShowSculptedGround(sculpted, sculpted.Parts);
    }

    /// <summary>Picks the sculptor (T), reading the zone's ground the first time.</summary>
    void ChooseSculpt()
    {
        if (EnsureSculpt()) SetTool(LevelTool.Sculpt);
        else SetTool(tool == LevelTool.Sculpt ? LevelTool.Move : tool);
    }

    bool EnsureSculpt()
    {
        if (sculpt != null) return true;
        if (!context.Assets.IndexReady)
        {
            context.Log("Sculpt: the editor is still reading the game's files (to find the zone's ground). Try again in a few seconds.");
            return false;
        }
        string name = TerrainZone;
        byte[]? Read(string file) => context.Assets.ReadFile(file, archive, container)?.Data;
        sculpt = TerrainSculpt.Load(name, Read, out string? why);
        if (sculpt == null)
        {
            MessageBox.Show($"The ground of {name} can't be sculpted: {why}.", "Level editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        // The zones around too, so a stroke near an edge carries on into them (and their far-away ground with them).
        sculptZones = [sculpt];
        var around = new List<string>();
        foreach (string neighbour in LevelGeometry.Neighbours(LevelGeometry.ZoneNames(zoneName).Last()).Select(GroundKey).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (neighbour.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            if (TerrainSculpt.Load(neighbour, Read, out _) is not { } next) continue;
            sculptZones.Add(next);
            around.Add(neighbour);
        }
        context.Log($"Sculpt: {name}'s ground, its height field ({sculpt.Step:0.###} m apart) and {sculpt.Parts.Count} sub-zones" +
                    (around.Count > 0 ? $", and the zones around it ({string.Join(", ", around)}), so strokes carry on over the edges" : "") +
                    ". Hold the left button to sculpt (Shift: the opposite), [ and ] for the brush's size. Saving writes each zone's terrain file, " +
                    "its far-away ground and the sub-zones changed, as overrides." +
                    (sculpt.FarProblem != null ? $" ({name}'s far-away ground can't follow: {sculpt.FarProblem}.)" : ""));
        return true;
    }

    void ResizeBrush(double factor) => SculptRadiusSlider.Value = Math.Clamp(SculptRadiusSlider.Value * factor, SculptRadiusSlider.Minimum, SculptRadiusSlider.Maximum);

    void OnBrushChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsLoaded) ShowBrush();
    }

    /// <summary>The ground's height at a place: in whichever sculpted zone it is (this zone's edge beyond them).</summary>
    float GroundHeight(float x, float z) => (sculptZones.FirstOrDefault(s => s.Covers(x, z)) ?? sculpt)?.HeightAt(x, z) ?? 0;

    /// <summary>Where the brush is on the ground (the game's space): under the mouse, or the middle of the view for the controller; null if not over the ground.</summary>
    Vector3? BrushPoint()
    {
        if (sculpt == null || ViewArea.ActualWidth <= 0) return null;
        Point point = !strokeFromPad && brushAt is Point mouse && !padSculpting ? mouse : new Point(ViewArea.ActualWidth / 2, ViewArea.ActualHeight / 2);
        (Point3D origin, Vector3D direction) = camera.Ray(point, ViewArea.RenderSize);
        var from = new Vector3((float)origin.X, (float)origin.Y, (float)-origin.Z);
        var way = new Vector3((float)direction.X, (float)direction.Y, (float)-direction.Z);
        Vector3? nearest = null;
        foreach (TerrainSculpt zone in sculptZones)
            if (zone.Hit(from, way) is Vector3 hit && (nearest is not Vector3 best || Vector3.DistanceSquared(hit, from) < Vector3.DistanceSquared(best, from)))
                nearest = hit;
        return nearest;
    }

    /// <summary>Draws the brush: a ring on the ground, its radius across.</summary>
    void ShowBrush()
    {
        if (tool != LevelTool.Sculpt || sculpt == null || BrushPoint() is not Vector3 centre)
        {
            brushVisual.Content = null;
            return;
        }
        float radius = BrushRadius, width = MathF.Max(0.15f, radius * 0.04f);
        const int segments = 64;
        var positions = new Point3DCollection(segments * 2 + 2);
        var triangles = new Int32Collection(segments * 6);
        for (int i = 0; i <= segments; i++)
        {
            double angle = i * 2 * Math.PI / segments;
            foreach (float r in new[] { radius - width, radius + width })
            {
                float x = centre.X + (float)Math.Cos(angle) * r, z = centre.Z + (float)Math.Sin(angle) * r;
                positions.Add(new Point3D(x, GroundHeight(x, z) + 0.3, -z));
            }
            if (i < segments)
            {
                int a = i * 2;
                triangles.Add(a); triangles.Add(a + 1); triangles.Add(a + 2);
                triangles.Add(a + 1); triangles.Add(a + 3); triangles.Add(a + 2);
            }
        }
        var mesh = new MeshGeometry3D { Positions = positions, TriangleIndices = triangles };
        mesh.Freeze();
        Color colour = ChosenMode switch
        {
            SculptMode.Lower or SculptMode.Valley => Color.FromRgb(90, 170, 255), SculptMode.Smooth => Color.FromRgb(140, 230, 140),
            SculptMode.Flatten => Color.FromRgb(230, 200, 90), SculptMode.Mountain => Color.FromRgb(200, 150, 255), SculptMode.Peak => Color.FromRgb(255, 90, 90),
            SculptMode.PaintFoliage => Color.FromRgb(120, 220, 90), SculptMode.EraseFoliage => Color.FromRgb(220, 220, 220),
            SculptMode.PaintGround => Color.FromRgb(255, 220, 120), SculptMode.PaintRoad => Color.FromRgb(170, 170, 190),
            _ => Color.FromRgb(255, 140, 60),
        };
        var material = new MaterialGroup { Children = { new DiffuseMaterial(new SolidColorBrush(Colors.Black)), new EmissiveMaterial(new SolidColorBrush(colour)) } };
        material.Freeze();
        brushVisual.Content = new GeometryModel3D(mesh, material) { BackMaterial = material };
    }

    /// <summary>Starts a stroke where the brush is; false if it isn't over the ground.</summary>
    bool BeginStroke(bool fromPad)
    {
        if (sculpt == null) return false;
        strokeFromPad = fromPad;
        if (BrushPoint() is not Vector3 centre)
        {
            strokeFromPad = false;
            return false;
        }
        CommitEdit();
        if ((ChosenMode is SculptMode.PaintFoliage or SculptMode.EraseFoliage && !BeginFoliageStroke()) ||
            (ChosenMode is SculptMode.PaintGround or SculptMode.PaintRoad && !BeginGroundStroke(ChosenMode == SculptMode.PaintRoad)))
        {
            strokeFromPad = false;
            return false;
        }
        strokeBefore = sculptZones.ToDictionary(z => z, z => z.Snapshot());
        standing = StandingOnGround(zone, GroundHeight, z => sculptZones.Any(s => s.Covers(z.X, z.Z)))
            .Where(o => Editable(o)).ToDictionary(o => o, o => o.Placement.Position.Y - GroundHeight(o.Placement.Position.X, o.Placement.Position.Z));
        strokeObjects.Clear();
        flattenTo = centre.Y;
        lastSculpt = DateTime.Now;
        CompositionTarget.Rendering -= SculptFrame;
        CompositionTarget.Rendering += SculptFrame;
        return true;
    }

    /// <summary>A step of the stroke, every frame while it goes on: over every zone the brush reaches.</summary>
    void SculptFrame(object? sender, EventArgs e)
    {
        if (sculpt == null || strokeBefore == null) return;
        DateTime now = DateTime.Now;
        float seconds = (float)Math.Min(0.1, (now - lastSculpt).TotalSeconds);
        lastSculpt = now;
        if (BrushPoint() is not Vector3 centre) return;
        SculptMode mode = ChosenMode;
        bool inverse = strokeFromPad ? padInverse : Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (mode is SculptMode.PaintFoliage or SculptMode.EraseFoliage)
        {
            // The foliage painter (LevelEditor.Foliage.cs): the opposite (Shift) erases while painting, and paints while erasing.
            PaintFoliage(new Vector2(centre.X, centre.Z), BrushRadius, erase: (mode == SculptMode.EraseFoliage) ^ inverse);
            ShowBrush();
            return;
        }
        if (mode is SculptMode.PaintGround or SculptMode.PaintRoad)
        {
            // The ground painter (LevelEditor.GroundPaint.cs): Shift paints the base layer back.
            GroundStrokeStep(new Vector2(centre.X, centre.Z), BrushRadius, (float)SculptStrengthSlider.Value * (strokeFromPad ? padStrength : 1) * seconds, inverse);
            ShowBrush();
            return;
        }
        if (inverse) mode = mode switch { SculptMode.Raise => SculptMode.Lower, SculptMode.Lower => SculptMode.Raise, _ => mode };
        float strength = (float)SculptStrengthSlider.Value * (strokeFromPad ? padStrength : 1);
        bool landform = mode is SculptMode.Hill or SculptMode.Peak or SculptMode.Mountain or SculptMode.Valley;
        float amount = mode is SculptMode.Raise or SculptMode.Lower || landform ? strength * seconds : Math.Min(1, strength * seconds * 0.5f);
        foreach (var (ground, parts) in TerrainSculpt.ApplyAll(sculptZones, new Vector2(centre.X, centre.Z), BrushRadius, mode, amount, flattenTo,
                                                               opposite: inverse && landform))
            ShowSculptedGround(ground, parts);
        FollowGround(new Vector2(centre.X, centre.Z), BrushRadius + 2);
        ShowBrush();
    }

    /// <summary>Ends a stroke: keeps it as one change, to undo (in every zone it reached).</summary>
    void EndStroke()
    {
        CompositionTarget.Rendering -= SculptFrame;
        bool wasPad = strokeFromPad;
        strokeFromPad = false;
        if (sculpt == null || strokeBefore is not { } before) return;
        if (EndFoliageStroke() || EndGroundStroke(before))
        {
            strokeBefore = null;
            standing = null;
            if (!wasPad) ShowBrush();
            return;
        }
        SculptedLive();  // the game has the ground as sculpted at once (LevelEditor.LiveTerrain.cs), or once the level is loaded again
        strokeBefore = null;
        standing = null;
        var changed = new List<(TerrainSculpt Zone, float[] Before, float[] After)>();
        foreach (var (ground, then) in before)
        {
            float[] now = ground.Snapshot();
            if (!now.AsSpan().SequenceEqual(then)) changed.Add((ground, then, now));
        }
        var objects = strokeObjects.Select(o => (Object: o.Key, Before: o.Value, After: o.Key.Snapshot())).ToList();
        strokeObjects.Clear();
        if (changed.Count == 0 && objects.Count == 0) return;
        void Put(bool back)
        {
            foreach (var c in changed) ShowSculptedGround(c.Zone, c.Zone.Restore(back ? c.Before : c.After));
            foreach (var o in objects)
            {
                o.Object.Restore(back ? o.Before : o.After);
                scene?.Place(o.Object);
            }
            if (objects.Count > 0) ShowSelection();
        }
        Record(new Change(null, $"Sculpt ({ChosenMode.ToString().ToLowerInvariant()})" + (changed.Count > 1 ? $" over {changed.Count} zones" : "") +
                                (objects.Count > 0 ? $", {objects.Count} object(s) moved with the ground" : ""),
            Undo: () => Put(back: true),
            Redo: () => Put(back: false)));
        if (!wasPad) ShowBrush();
    }

    /// <summary>Gives a zone's sculpted sub-zones' models new meshes, at their vertices' heights now (with their normals; the rest kept).</summary>
    void ShowSculptedGround(TerrainSculpt zone, IReadOnlyList<TerrainSculpt.Part> parts)
    {
        if (!groundModels.TryGetValue(zone.Zone, out var ground)) return;
        foreach (TerrainSculpt.Part part in parts)
        {
            int index = ground.Numbers.IndexOf(part.Number);
            if (index < 0 || index >= ground.Models.Children.Count || ground.Models.Children[index] is not GeometryModel3D { IsFrozen: false } model ||
                model.Geometry is not MeshGeometry3D old || old.Positions.Count != part.Heights.Length)
                continue;
            var positions = new Point3DCollection(part.Heights.Length);
            var normals = new Vector3DCollection(part.Heights.Length);
            for (int v = 0; v < part.Heights.Length; v++)
            {
                Vector3 p = part.Subzone.Positions[v], n = part.Normals[v];
                positions.Add(new Point3D(p.X, part.Heights[v], -p.Z));  // WPF's Z is the game's -Z
                normals.Add(new Vector3D(n.X, n.Y, -n.Z));
            }
            var mesh = new MeshGeometry3D
            {
                Positions = positions,
                Normals = normals,
                TextureCoordinates = old.TextureCoordinates.IsFrozen ? old.TextureCoordinates : old.TextureCoordinates.Clone(),
                TriangleIndices = old.TriangleIndices.IsFrozen ? old.TriangleIndices : old.TriangleIndices.Clone(),
            };
            mesh.Freeze();
            model.Geometry = mesh;
        }
    }

    /// <summary>
    /// The objects of a zone file standing on the ground: those that aren't attached to another
    /// (what is attached moves with them), over the sculpted ground, whose bounding box's bottom is
    /// within a few metres of the ground under their position (from 3 m in it to 2 m over it); a
    /// building, a crate or a navpoint, not a lamp on a pole or a light in a ceiling.
    /// </summary>
    static IEnumerable<ZoneObject> StandingOnGround(ZoneFile file, Func<float, float, float> ground, Func<Vector3, bool> over) =>
        file.Objects.Where(o => o.Parent == ZoneObject.None && !IsBarrier(o) && over(o.Placement.Position) &&
                                o.BoundsMin.Y - ground(o.Placement.Position.X, o.Placement.Position.Z) is >= -3 and <= 2);

    /// <summary>During a stroke: this zone's objects on the ground near the brush keep their height over it (with what is attached to them).</summary>
    void FollowGround(Vector2 centre, float reach)
    {
        if (standing == null) return;
        bool any = false;
        foreach (var (obj, above) in standing)
        {
            Vector3 p = obj.Placement.Position;
            if (Vector2.DistanceSquared(new Vector2(p.X, p.Z), centre) > reach * reach) continue;
            float by = GroundHeight(p.X, p.Z) + above - p.Y;
            if (MathF.Abs(by) < 0.001f) continue;
            foreach (ZoneObject moving in zone.WithAttached(obj).ToList())
            {
                if (!strokeObjects.ContainsKey(moving)) strokeObjects[moving] = moving.Snapshot();
                moving.MoveTo(moving.Placement.Position + new Vector3(0, by, 0));
                scene?.Place(moving);
            }
            any = true;
        }
        if (any && selected != null && strokeObjects.ContainsKey(selected)) ShowSelection();
    }

    /// <summary>
    /// When saving sculpted ground: what stands on it in the zone files this editor doesn't edit
    /// moves with it, by as much as the ground moved since it was last saved: this zone's other
    /// files (its persistent zone, its mission layers: moved where they are shown too) and the
    /// changed zones around's files, every copy. (This zone's own objects moved as it was sculpted.)
    /// Returns what was done, for the log.
    /// </summary>
    string MoveWhatStandsOnGround()
    {
        var changed = sculptZones.Where(z => z.Changed).ToList();
        if (changed.Count == 0) return "";
        TerrainSculpt? Under(Vector3 p) => changed.FirstOrDefault(z => z.Covers(p.X, p.Z));
        float Saved(float x, float z) => changed.FirstOrDefault(s => s.Covers(x, z))?.SavedHeightAt(x, z) ?? 0;
        int Move(ZoneFile file, Action<ZoneObject>? moved)
        {
            int count = 0;
            foreach (ZoneObject root in StandingOnGround(file, Saved, p => Under(p) != null).ToList())
            {
                Vector3 p = root.Placement.Position;
                TerrainSculpt ground = Under(p)!;
                float by = ground.HeightAt(p.X, p.Z) - ground.SavedHeightAt(p.X, p.Z);
                if (MathF.Abs(by) < 0.001f) continue;
                foreach (ZoneObject obj in file.WithAttached(root).ToList())
                {
                    obj.MoveTo(obj.Placement.Position + new Vector3(0, by, 0));
                    moved?.Invoke(obj);
                }
                count++;
            }
            return count;
        }
        var said = new List<string>();
        foreach (RelatedFile file in RelatedFiles())
        {
            int count = Move(file.Zone, obj => file.Scene?.Place(obj));
            if (count > 0) said.Add($"{count} in {file.Name} ({PutZoneFile(file.Name, file.Archive, file.Container, file.Zone.ToBytes())})");
        }
        var open = new HashSet<string>(RelatedFiles().Select(f => f.Name).Append(zoneName), StringComparer.OrdinalIgnoreCase);
        foreach (TerrainSculpt ground in changed.Where(z => !z.Zone.Equals(TerrainZone, StringComparison.OrdinalIgnoreCase)))
        {
            var names = context.Assets.NamesWhere(n => (n.EndsWith(".rfgzone_pc", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".layer_pc", StringComparison.OrdinalIgnoreCase)) &&
                                                       GroundKey(LevelGeometry.ZoneNames(n).Last()).Equals(ground.Zone, StringComparison.OrdinalIgnoreCase) && !open.Contains(n)).ToList();
            foreach (string name in names)
                foreach ((string inArchive, string? inContainer) in context.Assets.PlacesOf(name).Distinct())
                {
                    try
                    {
                        byte[] bytes = inContainer == null ? Workspace.Read(inArchive, name)
                            : Workspace.Container(inArchive, inContainer) is var pack && pack.Find(name) is { } entry ? pack.Read(entry) : throw new System.IO.FileNotFoundException(name);
                        ZoneFile file = ZoneFile.Read(bytes);
                        int count = Move(file, null);
                        if (count > 0) said.Add($"{count} in {name} ({PutZoneFile(name, inArchive, inContainer, file.ToBytes())})");
                    }
                    catch (Exception ex) when (ex is System.IO.InvalidDataException or System.IO.FileNotFoundException or NotSupportedException)
                    {
                        said.Add($"{name} couldn't be read ({ex.Message})");
                    }
                }
        }
        return said.Count == 0 ? "" : " Objects on the ground moved with it: " + string.Join("; ", said) + ".";
    }

    /// <summary>The zone whose ground this is (its terrain files are named after it).</summary>
    string TerrainZone => GroundKey(LevelGeometry.ZoneNames(zoneName).Last());

    /// <summary>Every file of a zone's ground the sculptor may change: the terrain file, its far-away ground's buffers, and the nine sub-zones' two files.</summary>
    static IEnumerable<string> TerrainFileNames(string zone) =>
        new[] { zone + ".cterrain_pc", zone + ".gterrain_pc" }.Concat(Enumerable.Range(0, 9).SelectMany(n => new[] { TerrainSubzone.HeaderFileName(zone, n), TerrainSubzone.DataFileName(zone, n) }));

    /// <summary>
    /// Writes the sculpted ground of every zone changed (its terrain file, far-away ground and the
    /// sub-zones changed) wherever the game has them, always as overrides: rebuilt containers and
    /// files in overrides\ (the Guerrilla world's in edits\); the archives stay as they are. A file
    /// sculpted back to its own shape stops being overridden. Returns what was done, for the log.
    /// </summary>
    string SaveSculpt()
    {
        var said = new List<string>();
        foreach (TerrainSculpt zone in sculptZones.Where(z => z.Changed))
        {
            string written = PutTerrainFiles(zone.Files());
            var also = new List<string>();
            if (zone.FarMoved > 0) also.Add($"{zone.FarMoved:N0} far-away vertices");
            if (zone.RoadsMoved > 0) also.Add($"{zone.RoadsMoved:N0} road vertices");
            if (zone.FoliageMoved > 0) also.Add($"{zone.FoliageMoved:N0} cubes of foliage" + (zone.FoliageLost > 0 ? $" ({zone.FoliageLost:N0} more had no cell to go to, so they grow no more)" : ""));
            said.Add($"{zone.Zone}'s ground: {written}" + (also.Count > 0 ? "; moved with it: " + string.Join(", ", also) : "") +
                     (zone.FarProblem != null ? $" (its far-away ground can't follow: {zone.FarProblem})" : ""));
        }
        return said.Count == 0 ? "" : string.Join(". ", said) + ".";
    }

    /// <summary>Puts ground files in every place the game has them: each container rebuilt once (or the game's own again, if all its files are), each loose file overridden (or not, if it is the game's own).</summary>
    string PutTerrainFiles(IReadOnlyDictionary<string, byte[]> files)
    {
        var inContainers = new Dictionary<(string Archive, string Container), Dictionary<string, byte[]>>();
        var done = new List<string>();
        foreach (var (name, bytes) in files)
        {
            var places = context.Assets.PlacesOf(name).ToList();
            if (places.Count == 0) done.Add($"{name} (not found: not written)");
            foreach (var (inArchive, inContainer) in places)
            {
                if (inContainer == null)
                {
                    // Loose in its archive: the asset lists give its size (the game sets memory aside by it,
                    // and a level whose terrain file is bigger or smaller than its list says never finishes
                    // loading), so they follow it: from its size now, and from the game's own size (lists an
                    // earlier save left behind).
                    byte[]? original = TryOriginal(inArchive, null, name);
                    int sizeNow = Workspace.Read(inArchive, name).Length;
                    var lists = ContainerEdits.UpdateLooseSize(Workspace, name, sizeNow, bytes.Length);
                    if (original != null && original.Length != sizeNow) lists.AddRange(ContainerEdits.UpdateLooseSize(Workspace, name, original.Length, bytes.Length));
                    bool own = original != null && original.AsSpan().SequenceEqual(bytes);
                    if (own) Workspace.DeleteOverride(inArchive, name);
                    else Workspace.WriteOverride(inArchive, name, bytes);
                    string sized = lists.Count > 0 ? $" (its size updated in {string.Join(", ", lists.Distinct(StringComparer.OrdinalIgnoreCase))})" : "";
                    done.Add((own ? $"{name} is the game's own again" : name) + sized);
                    continue;
                }
                if (!inContainers.TryGetValue((inArchive, inContainer), out var group)) inContainers[(inArchive, inContainer)] = group = new(StringComparer.OrdinalIgnoreCase);
                group[name] = bytes;
            }
        }
        foreach (var ((inArchive, inContainer), group) in inContainers)
            done.Add(ContainerEdits.Put(Workspace, inArchive, inContainer, group));
        return done.Count == 0 ? "nothing to write" : string.Join("; ", done);
    }

    /// <summary>The game's own copy of a file (in a container, or loose in an archive); null if it had none.</summary>
    byte[]? TryOriginal(string inArchive, string? inContainer, string name)
    {
        try
        {
            if (inContainer == null) return Workspace.ReadOriginal(inArchive, name);
            Packfile original = Packfile.FromBytes(inContainer, Workspace.ReadOriginal(inArchive, inContainer));
            return original.Find(name) is { } entry ? original.Read(entry) : null;
        }
        catch (Exception ex) when (ex is System.IO.FileNotFoundException or System.IO.InvalidDataException) { return null; }
    }

    /// <summary>Whether any of the zone's ground files is overridden (or changed in one of our archives).</summary>
    bool TerrainChanged() =>
        context.Assets.IndexReady &&
        TerrainFileNames(TerrainZone).Any(name => context.Assets.PlacesOf(name).Any(p => Workspace.IsChanged(p.Archive, p.Container ?? name)));

    /// <summary>Puts the zone's ground back as the game has it: every ground file that differs from the game's own, in every place. Returns what was done ("" if nothing).</summary>
    string RestoreTerrain()
    {
        var notes = new List<string>();
        var inContainers = new Dictionary<(string Archive, string Container), Dictionary<string, byte[]>>();
        foreach (string name in TerrainFileNames(TerrainZone))
            foreach (var (inArchive, inContainer) in context.Assets.PlacesOf(name))
            {
                if (!Workspace.IsChanged(inArchive, inContainer ?? name)) continue;
                if (inContainer == null)
                {
                    // (Its size in the asset lists back to the game's own too.)
                    int sizeNow = Workspace.Read(inArchive, name).Length;
                    if (TryOriginal(inArchive, null, name) is byte[] own) ContainerEdits.UpdateLooseSize(Workspace, name, sizeNow, own.Length);
                    Workspace.DeleteOverride(inArchive, name);
                    notes.Add($"{name} is the game's own again");
                    continue;
                }
                if (TryOriginal(inArchive, inContainer, name) is not byte[] original) continue;
                Packfile now = Workspace.Container(inArchive, inContainer);
                if (now.Find(name) is { } entry && now.Read(entry).AsSpan().SequenceEqual(original)) continue;
                if (!inContainers.TryGetValue((inArchive, inContainer), out var group)) inContainers[(inArchive, inContainer)] = group = new(StringComparer.OrdinalIgnoreCase);
                group[name] = original;
            }
        foreach (var ((inArchive, inContainer), group) in inContainers)
            notes.Add(ContainerEdits.Put(Workspace, inArchive, inContainer, group));
        return notes.Count == 0 ? "" : "The ground: " + string.Join("; ", notes) + ".";
    }

    // ---- Controller ----------------------------------------------------------------------------
    //
    // A free-flying camera with a reticle in the middle of the view: what the reticle is on is what
    // the buttons act on. A grabs it (one press) and A again lets go; while held it stays where it
    // is from the view as you fly and look, and holding RT turns it and pushes it away or pulls it
    // nearer instead of moving the camera.

    readonly Gamepad pad = new();
    bool padWasConnected, carrying, padSculpting;
    ZoneObject? carryObject, padTarget;
    float carryDistance, carrySide, carryLift;   // where the held object is from the view: ahead, right and up
    Matrix4x4 carryBase, carryTurn = Matrix4x4.Identity;
    DateTime lastPadFrame = DateTime.Now, nextRepeat, nextStickRepeat, nextTargetLook;
    double padZoom = 1;                          // the field of view's share while zoomed (the right stick pressed)
    enum ReticleLook { Off, Free, OnObject, OnLocked, Holding }
    ReticleLook reticleShown = ReticleLook.Off;

    void OnSwapController(object sender, RoutedEventArgs e) => GamePanel.SwapController();

    void ShowControllerOwner() => ControllerButtonText.Text = GamePanel.ControllerLabel;

    void PadFrame(object? sender, EventArgs e)
    {
        DateTime now = DateTime.Now;
        float seconds = (float)Math.Min(0.1, (now - lastPadFrame).TotalSeconds);
        lastPadFrame = now;
        if (!IsVisible || Window.GetWindow(this)?.IsActive != true) return;
        bool read = pad.Read(out PadState state, out PadButtons pressed);
        // Both sticks pressed in: the controller changes hands, between this view and the docked game.
        if (read && state.Holds(PadButtons.LeftStick) && state.Holds(PadButtons.RightStick) &&
            (pressed & (PadButtons.LeftStick | PadButtons.RightStick)) != 0)
        {
            GamePanel.SwapController();
            return;
        }
        if (GamePanel.ControllerToGame) return;  // the controller is the docked game's
        if (!read)
        {
            if (padWasConnected)
            {
                context.Log("Controller: disconnected.");
                if (carrying) DropHeld();
                if (strokeFromPad) EndStroke();
                SetZoom(1);
                ShowReticle(ReticleLook.Off, null);
            }
            padWasConnected = false;
            return;
        }
        if (!padWasConnected)
        {
            padWasConnected = true;
            context.Log("Controller: connected. Left stick moves, right stick looks, LB / RB fly down and up, LT goes faster, " +
                        "the right stick pressed zooms. A grabs what the reticle is on and A again lets go; hold RT while holding it to " +
                        "turn it with the right stick and push it away or pull it nearer with the left. X duplicates what the reticle is on " +
                        "(or opens the Add list on nothing: D-pad / left stick to scroll, LB / RB a page, A opens a folder or adds the object in your hand, B goes back or closes), " +
                        "Y deletes, B puts a held object back (or switches to the sculptor and back), D-pad up snaps, the left stick pressed turns magnets on and off, left / right turn 15°, " +
                        "down looks at the picked object. View undoes, Menu saves.");
        }

        if (AddPopup.IsOpen) { PadAddList(state, pressed, now); return; }
        if (pressed.HasFlag(PadButtons.Back)) { if (carrying) DropHeld(); Undo(); }
        if (pressed.HasFlag(PadButtons.Start)) { if (carrying) DropHeld(); Save(); }

        bool manipulating = carrying && tool != LevelTool.Sculpt && state.RightTrigger > 0.2f;
        bool moved = false;
        if (!manipulating) moved = PadFly(state, seconds);
        SetZoom(state.Holds(PadButtons.RightStick) ? 0.4 : 1);

        if (tool != LevelTool.Sculpt && strokeFromPad) EndStroke();
        if (tool == LevelTool.Sculpt)
        {
            ShowReticle(ReticleLook.Free, null);
            PadSculpt(state, pressed, moved);
        }
        else PadObjects(state, pressed, now, seconds, manipulating);
    }

    /// <summary>Flying and looking; true if the camera moved.</summary>
    bool PadFly(PadState state, float seconds)
    {
        Vector3D way = camera.Forward * state.LeftY + camera.Right * state.LeftX;
        if (state.Holds(PadButtons.RightShoulder)) way += new Vector3D(0, 1, 0);
        if (state.Holds(PadButtons.LeftShoulder)) way -= new Vector3D(0, 1, 0);
        bool moved = false;
        if (way.Length > 1e-6)
        {
            // LT goes faster (in the sculptor, where LT lowers, the left stick pressed does).
            double faster = tool == LevelTool.Sculpt ? (state.Holds(PadButtons.LeftStick) ? 5 : 1) : 1 + 4 * state.LeftTrigger;
            camera.Position += way * 25 * faster * seconds;
            moved = true;
        }
        if (state.RightX != 0 || state.RightY != 0)
        {
            double slower = padZoom < 1 ? 0.4 : 1;
            camera.Turn(-state.RightX * 2.4 * slower * seconds, state.RightY * 1.8 * slower * seconds);
            moved = true;
        }
        if (moved) UpdateCamera();
        return moved;
    }

    void SetZoom(double share)
    {
        if (Math.Abs(padZoom - share) < 1e-9) return;
        padZoom = share;
        UpdateCamera();
    }

    bool Repeats(PadState state, PadButtons pressed, PadButtons button, DateTime now)
    {
        if (pressed.HasFlag(button)) { nextRepeat = now.AddSeconds(0.35); return true; }
        if (state.Holds(button) && now >= nextRepeat) { nextRepeat = now.AddSeconds(0.08); return true; }
        return false;
    }

    /// <summary>A stick pushed past half way as a repeating button: -1, 0 or 1.</summary>
    int StickSteps(float value, DateTime now, double first = 0.3, double then = 0.1)
    {
        if (MathF.Abs(value) < 0.5f) { nextStickRepeat = default; return 0; }
        if (now < nextStickRepeat) return 0;
        nextStickRepeat = now.AddSeconds(nextStickRepeat == default ? first : then);
        return Math.Sign(value);
    }

    // ---- Sculpting with the controller

    void PadSculpt(PadState state, PadButtons pressed, bool cameraMoved)
    {
        if (sculpt == null) return;
        if (cameraMoved || pressed != PadButtons.None || state.LeftTrigger > 0.05f || state.RightTrigger > 0.05f) padSculpting = true;
        float trigger = MathF.Max(state.RightTrigger, state.LeftTrigger);
        if (trigger > 0.05f)
        {
            padInverse = state.LeftTrigger > state.RightTrigger;
            padStrength = trigger;
            if (strokeBefore == null) BeginStroke(fromPad: true);
        }
        else if (strokeFromPad) EndStroke();
        if (pressed.HasFlag(PadButtons.A)) SculptModeBox.SelectedIndex = (SculptModeBox.SelectedIndex + 1) % 4;
        if (pressed.HasFlag(PadButtons.DpadRight)) ResizeBrush(1.25);
        if (pressed.HasFlag(PadButtons.DpadLeft)) ResizeBrush(1 / 1.25);
        if (pressed.HasFlag(PadButtons.DpadUp)) SculptStrengthSlider.Value = Math.Min(SculptStrengthSlider.Maximum, SculptStrengthSlider.Value * 1.25);
        if (pressed.HasFlag(PadButtons.DpadDown)) SculptStrengthSlider.Value = Math.Max(SculptStrengthSlider.Minimum, SculptStrengthSlider.Value / 1.25);
        if (pressed.HasFlag(PadButtons.B))
        {
            if (strokeFromPad) EndStroke();
            padSculpting = false;
            SetTool(LevelTool.Move);
            return;
        }
        if (padSculpting && (cameraMoved || pressed != PadButtons.None)) ShowBrush();
    }

    // ---- Objects with the controller

    Point ViewMiddle => new(ViewArea.ActualWidth / 2, ViewArea.ActualHeight / 2);

    /// <summary>The object under the reticle (as clicking there would pick), or null.</summary>
    ZoneObject? ObjectAtMiddle()
    {
        if (ViewArea.ActualWidth <= 0) return null;
        foreach (RayMeshGeometry3DHitTestResult hit in HitsAt(ViewMiddle, sceneContent, relatedContent, geometryContent, terrainContent))
        {
            if (hit.VisualHit == geometryContent || hit.VisualHit == terrainContent) return null;
            if (ObjectOf(hit) is ZoneObject found) return found;
        }
        return null;
    }

    void PadObjects(PadState state, PadButtons pressed, DateTime now, float seconds, bool manipulating)
    {
        if (carrying)
        {
            PadHolding(state, pressed, now, seconds, manipulating);
            return;
        }

        // What the reticle is on: looked for ten times a second, or when a button is pressed.
        if (now >= nextTargetLook || pressed != PadButtons.None)
        {
            nextTargetLook = now.AddSeconds(0.1);
            padTarget = ObjectAtMiddle();
        }
        ZoneObject? target = padTarget;
        ShowReticle(target == null ? ReticleLook.Free : Editable(target) ? ReticleLook.OnObject : ReticleLook.OnLocked, target);

        if (pressed.HasFlag(PadButtons.A))
        {
            // One press: grab what the reticle is on.
            PickInOutliner(target);
            if (target != null && selected == target)
            {
                if (Editable(target)) StartCarrying();
                else context.Log($"Controller: {Label(target)} belongs to another file shown here (it can't be moved from this one).");
            }
        }
        else if (pressed.HasFlag(PadButtons.X))
        {
            if (target != null && Editable(target))
            {
                PickInOutliner(target);
                ZoneObject? before = selected;
                DuplicateSelected();
                if (selected != null && selected != before) StartCarrying();
            }
            else OpenAddListFromPad();
        }
        else if (pressed.HasFlag(PadButtons.Y))
        {
            if (target != null && Editable(target))
            {
                PickInOutliner(target);
                DeleteSelected();
                padTarget = null;
            }
        }
        else if (pressed.HasFlag(PadButtons.B)) ChooseSculpt();
        else if (pressed.HasFlag(PadButtons.DpadUp)) ToggleSnap();
        else if (pressed.HasFlag(PadButtons.LeftStick)) ToggleMagnets();
        else if (pressed.HasFlag(PadButtons.DpadDown)) { if (target != null) PickInOutliner(target); FocusOnSelected(); }
        else if ((pressed.HasFlag(PadButtons.DpadLeft) || pressed.HasFlag(PadButtons.DpadRight)) && target != null && Editable(target) && target.CanTurn)
        {
            PickInOutliner(target);
            int turn = pressed.HasFlag(PadButtons.DpadLeft) ? 1 : -1;
            CommitEdit();
            StartEdit();
            TurnSelected(selected!.Placement.Rotation * Matrix4x4.CreateRotationY(turn * MathF.PI / 12), fromInspector: false);
            CommitEdit("Turn");
        }
    }

    void ToggleSnap()
    {
        SnapBox.IsChecked = SnapBox.IsChecked != true;
        context.Log($"Controller: snap {(Snapping ? $"on ({GridSize:0.##} m, 15°)" : "off")}.");
    }

    void PadHolding(PadState state, PadButtons pressed, DateTime now, float seconds, bool manipulating)
    {
        if (selected == null || selected != carryObject || !Editable(selected)) { carrying = false; return; }
        ShowReticle(ReticleLook.Holding, selected);

        if (pressed.HasFlag(PadButtons.A)) { DropHeld(); return; }
        if (pressed.HasFlag(PadButtons.B)) { PutBackHeld(); return; }
        if (pressed.HasFlag(PadButtons.Y)) { PutBackHeld(); DeleteSelected(); return; }
        if (pressed.HasFlag(PadButtons.DpadUp)) ToggleSnap();
        if (pressed.HasFlag(PadButtons.LeftStick)) ToggleMagnets();

        bool turned = false;
        if (selected.CanTurn)
        {
            if (pressed.HasFlag(PadButtons.DpadLeft)) { carryTurn *= Matrix4x4.CreateRotationY(MathF.PI / 12); turned = true; }
            if (pressed.HasFlag(PadButtons.DpadRight)) { carryTurn *= Matrix4x4.CreateRotationY(-MathF.PI / 12); turned = true; }
        }
        if (manipulating)
        {
            // RT held: the left stick pushes it away and pulls it nearer, the right stick turns it
            // (left and right about the up axis, up and down about the view's side to side).
            carryDistance = Math.Clamp(carryDistance + state.LeftY * 20 * seconds, 1, 400);
            if (selected.CanTurn)
            {
                Vector3 side = Vector3.Normalize(new Vector3((float)camera.Right.X, (float)camera.Right.Y, (float)-camera.Right.Z));
                if (Snapping)
                {
                    // In 15° steps.
                    int yaw = StickSteps(state.RightX, now), pitch = yaw == 0 ? StickSteps(state.RightY, now) : 0;
                    if (yaw != 0) { carryTurn *= Matrix4x4.CreateRotationY(-yaw * MathF.PI / 12); turned = true; }
                    if (pitch != 0) { carryTurn *= Matrix4x4.CreateFromAxisAngle(side, pitch * MathF.PI / 12); turned = true; }
                }
                else if (state.RightX != 0 || state.RightY != 0)
                {
                    carryTurn *= Matrix4x4.CreateRotationY(-state.RightX * 1.6f * seconds) *
                                 Matrix4x4.CreateFromAxisAngle(side, state.RightY * 1.6f * seconds);
                    turned = true;
                }
            }
        }
        if (turned) TurnSelected(carryBase * carryTurn, fromInspector: false);

        Point3D at = camera.Position + camera.Forward * carryDistance + camera.Right * carrySide + camera.Up * carryLift;
        var target = new Vector3((float)at.X, (float)at.Y, (float)-at.Z);
        // Magnets first: touching the models near it; else Snap.
        if (Magnets && MagnetSnap(selected, target, selected.Placement.Rotation) is Vector3 touching) target = touching;
        else if (Snapping)
        {
            float step = (float)GridSize;
            target = new Vector3(MathF.Round(target.X / step) * step, MathF.Round(target.Y / step) * step, MathF.Round(target.Z / step) * step);
        }
        if (Vector3.DistanceSquared(target, selected.Placement.Position) > 1e-6f) MoveSelected(target, fromInspector: false);
    }

    void StartCarrying()
    {
        if (selected == null) return;
        CommitEdit();
        StartEdit();
        carrying = true;
        carryObject = selected;
        carryTurn = Matrix4x4.Identity;
        carryBase = selected.Placement.Rotation;
        // Kept where it is from the view as the view moves and turns: so many metres ahead, right and up.
        Vector3D offset = ToView(selected.Placement.Position) - camera.Position;
        carryDistance = (float)Math.Clamp(Vector3D.DotProduct(offset, camera.Forward), 1, 400);
        carrySide = (float)Vector3D.DotProduct(offset, camera.Right);
        carryLift = (float)Vector3D.DotProduct(offset, camera.Up);
        ShowReticle(ReticleLook.Holding, selected);
    }

    /// <summary>Lets go of the held object where it is (one step to undo).</summary>
    void DropHeld()
    {
        carrying = false;
        ShowMagnet(null);
        if (selected == null) return;
        CommitEdit("Move");
        if (!InZone(selected.Placement.Position))
            context.Log($"Controller: {Label(selected)} is outside this zone, in {ZoneAt(selected.Placement.Position)}: the game drops it there. Move it back before saving.");
    }

    /// <summary>Puts the held object back where it was grabbed (nothing kept to undo).</summary>
    void PutBackHeld()
    {
        carrying = false;
        ShowMagnet(null);
        if (selected != null && editStart is byte[] start)
        {
            selected.Restore(start);
            foreach (Carried c in carried) c.Object.Restore(c.Start);
            AfterMoveOrTurn(fromInspector: false);
        }
        StartEdit();
    }

    // ---- The Add list with the controller

    void OpenAddListFromPad()
    {
        OnAddClicked(this, new RoutedEventArgs());
        StepAddList(1, fromStart: true);
    }

    void PadAddList(PadState state, PadButtons pressed, DateTime now)
    {
        ShowReticle(ReticleLook.Free, null);
        // B: up a level in the chunk browser, or closes the list; X closes it.
        if (pressed.HasFlag(PadButtons.B) && chunkPath.Count > 0) { ChunkFolderUp(); return; }
        if (pressed.HasFlag(PadButtons.B) || pressed.HasFlag(PadButtons.X)) { AddPopup.IsOpen = false; ViewArea.Focus(); return; }
        if (pressed.HasFlag(PadButtons.A) && AddList.SelectedItem is ListBoxItem { Tag: { } chosen })
        {
            ZoneObject? before = selected;
            AddFromList(chosen);  // (a folder of chunks: goes into it)
            if (!AddPopup.IsOpen && selected != null && selected != before && Editable(selected)) StartCarrying();  // the new object, in your hand
            return;
        }
        int step = Repeats(state, pressed, PadButtons.DpadDown, now) ? 1 : Repeats(state, pressed, PadButtons.DpadUp, now) ? -1 : -StickSteps(state.LeftY, now, 0.3, 0.07);
        if (pressed.HasFlag(PadButtons.RightShoulder)) step = 10;
        if (pressed.HasFlag(PadButtons.LeftShoulder)) step = -10;
        if (step != 0) StepAddList(step);
    }

    /// <summary>Moves the Add list's choice by so many entries (headings and notes skipped), and shows it.</summary>
    void StepAddList(int by, bool fromStart = false)
    {
        var items = AddList.Items.OfType<ListBoxItem>().ToList();
        var choices = Enumerable.Range(0, items.Count).Where(i => items[i].IsEnabled && items[i].Tag != null).ToList();
        if (choices.Count == 0) return;
        int now = fromStart ? -1 : choices.IndexOf(AddList.SelectedIndex);
        int next = now < 0 ? (by > 0 ? 0 : choices.Count - 1) : Math.Clamp(now + by, 0, choices.Count - 1);
        AddList.SelectedIndex = choices[next];
        AddList.ScrollIntoView(AddList.SelectedItem);
    }

    // ---- The reticle

    /// <summary>Draws the reticle in the middle of the view: a ring with four marks, closed in on a held object; the name of what it's on below.</summary>
    void ShowReticle(ReticleLook look, ZoneObject? on)
    {
        PadTargetText.Text = on == null ? "" : look == ReticleLook.OnLocked ? $"{Label(on)} (another file's)" : Label(on);
        PadTargetLabel.Visibility = on == null || look == ReticleLook.Off ? Visibility.Collapsed : Visibility.Visible;
        if (look == reticleShown) return;
        reticleShown = look;
        PadReticle.Children.Clear();
        PadReticle.Visibility = look == ReticleLook.Off ? Visibility.Collapsed : Visibility.Visible;
        if (look == ReticleLook.Off) return;
        Color colour = look switch
        {
            ReticleLook.OnObject => Color.FromRgb(90, 200, 255),
            ReticleLook.OnLocked => Color.FromRgb(150, 150, 150),
            ReticleLook.Holding => Color.FromRgb(255, 160, 60),
            _ => Color.FromArgb(220, 255, 255, 255),
        };
        var brush = new SolidColorBrush(colour);
        var shadow = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
        const double middle = 32;
        double ring = look == ReticleLook.Holding ? 9 : look == ReticleLook.Free ? 14 : 12;
        double gap = look == ReticleLook.Holding ? 3 : 8, mark = look == ReticleLook.Holding ? 9 : 7;
        void Add(System.Windows.Shapes.Shape shape, double left, double top)
        {
            Canvas.SetLeft(shape, left);
            Canvas.SetTop(shape, top);
            PadReticle.Children.Add(shape);
        }
        foreach ((SolidColorBrush paint, double width, double nudge) in new[] { (shadow, 3.5, 0.0), (brush, 1.5, 0.0) })
        {
            Add(new System.Windows.Shapes.Ellipse { Width = ring * 2, Height = ring * 2, Stroke = paint, StrokeThickness = width }, middle - ring, middle - ring);
            // The grabber's four marks, pointing in (closed in while holding).
            foreach ((double dx, double dy) in new[] { (1.0, 0.0), (-1.0, 0.0), (0.0, 1.0), (0.0, -1.0) })
            {
                double from = ring + gap, to = ring + gap + mark;
                Add(new System.Windows.Shapes.Line
                {
                    X1 = middle + dx * from, Y1 = middle + dy * from, X2 = middle + dx * to, Y2 = middle + dy * to,
                    Stroke = paint, StrokeThickness = width + 0.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                }, 0, 0);
            }
            double dot = look == ReticleLook.Holding ? 3 : 1.5;
            Add(new System.Windows.Shapes.Ellipse { Width = dot * 2 + width - 1.5, Height = dot * 2 + width - 1.5, Fill = paint }, middle - dot - (width - 1.5) / 2, middle - dot - (width - 1.5) / 2);
        }
    }
}
