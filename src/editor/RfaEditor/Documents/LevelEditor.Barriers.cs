using System.Numerics;
using System.Windows;
using System.Windows.Media.Media3D;
using RfaFormats;
using RfaFormats.Meshes;
using RfaFormats.Zones;

namespace RfaEditor;

// The zone's invisible walls: the collision shapes nothing draws that keep the player out of places,
// kept in its terrain file (<zone>.cterrain_pc, see TerrainBarriers: Armageddon's arena ruin01 has 28,
// its world zones up to 146). Volition's editor made them from invisible_wall and collision_box
// objects (CLOE_ONLY: never in the zone file), so the game has only the shapes. While the zone is open
// each shape is an object of the editor's own class "invisible_barrier" (like the level geometry's
// pieces, see LevelEditor.Geometry.cs), so it can be picked, moved, turned, copied and deleted as any
// object, and new ones added (a wall: a box, 10 m wide, 10 m high and 1 m thick, from the Add list).
// They are drawn with the helpers (red, LevelEditor.Helpers.cs). On saving, the shapes as they are now
// (the game's own moved and turned with their objects, the new ones boxes as big as their "bb") go
// back into the terrain file, in every copy; the game shows them after the level is loaded again.
public partial class LevelEditor
{
    static readonly uint BarrierClass = NameHashes.ClassHash("invisible_barrier");
    const uint BarrierHandles = 0xFFFD0000;  // the walls' handles while the zone is open (no zone uses these)
    static readonly Vector3 NewWallMin = new(-5, 0, -0.5f), NewWallMax = new(5, 10, 0.5f);

    static bool IsBarrier(ZoneObject obj) => obj.ClassHash == BarrierClass;

    /// <summary>"An invisible wall" in the Add list.</summary>
    sealed record BarrierChoice;

    bool barriersLoaded;
    string? barrierFile;                 // <zone>.cterrain_pc, if the zone has one
    Vector3 barrierOrigin;               // the shapes are kept from the zone's middle (x and z; y as it is)
    readonly Dictionary<uint, (ChunkShape Shape, Vector3 Centre)> barrierShapes = [];  // the game's own walls: the shape as read, and where its object was
    string barrierSignature = "";        // the walls as read or last saved: whether they changed

    /// <summary>The zone's invisible walls as objects in it (once, before the scene is made).</summary>
    void EnsureBarrierObjects()
    {
        if (barriersLoaded) return;
        string name = TerrainZone + ".cterrain_pc";
        if (context.Assets.ReadFile(name, archive, container) is not { } file)
        {
            if (context.Assets.IndexReady) barriersLoaded = true;  // (none: a zone without ground)
            return;
        }
        barriersLoaded = true;
        List<ChunkShape> shapes;
        try { shapes = TerrainBarriers.Read(file.Data); }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or NotSupportedException or ArgumentException or IndexOutOfRangeException)
        {
            context.Log($"{name}: its invisible walls couldn't be read ({ex.Message}).");
            return;
        }
        barrierFile = name;
        barrierOrigin = ZoneMiddle();
        var taken = zone.Objects.Select(o => o.Handle).ToHashSet();
        uint handle = BarrierHandles;
        foreach (ChunkShape shape in shapes)
        {
            while (taken.Contains(handle)) handle++;
            (Vector3 min, Vector3 max) = ShapeBounds(shape);
            Vector3 middle = (min + max) / 2;
            Vector3 centre = barrierOrigin + middle;
            NewBarrierObject(handle, centre, Matrix4x4.Identity, min - middle, max - middle);
            barrierShapes[handle] = (shape, centre);
            handle++;
        }
        barrierSignature = BarrierSignature();
        if (shapes.Count > 0) context.Log($"{zoneName}: {shapes.Count} invisible walls from {name} (drawn red with the helpers; they can be picked, moved, deleted and added like objects).");
    }

    /// <summary>Where the zone's terrain file keeps its walls from: the middle of its square (x and z), else the zone object's place.</summary>
    Vector3 ZoneMiddle()
    {
        if (ZoneSquare() is { } square) return new Vector3(square.Corner.X + square.Width / 2, 0, square.Corner.Y + square.Width / 2);
        uint zoneClass = NameHashes.ClassHash("obj_zone");
        return zone.Objects.FirstOrDefault(o => o.ClassHash == zoneClass)?.Placement.Position ?? Vector3.Zero;
    }

    /// <summary>A wall as an object: placed so, its box ("bb") from its place.</summary>
    ZoneObject NewBarrierObject(uint handle, Vector3 position, Matrix4x4 rotation, Vector3 min, Vector3 max)
    {
        var placement = new byte[48];
        float[] values = [position.X, position.Y, position.Z, rotation.M11, rotation.M12, rotation.M13, rotation.M21, rotation.M22, rotation.M23, rotation.M31, rotation.M32, rotation.M33];
        for (int i = 0; i < values.Length; i++) BitConverter.TryWriteBytes(placement.AsSpan(i * 4), values[i]);
        var box = new byte[24];
        float[] corners = [min.X, min.Y, min.Z, max.X, max.Y, max.Z];
        for (int i = 0; i < corners.Length; i++) BitConverter.TryWriteBytes(box.AsSpan(i * 4), corners[i]);
        var properties = new List<NewProperty>
        {
            new(ZoneProperty.ValueType, NameHashes.PropertyHash("oP"), placement),
            new(ZoneProperty.ValueType, NameHashes.PropertyHash("bb"), box),
        };
        (Vector3 low, Vector3 high) = StitchList.PlacedBox(min, max, RotationOf(rotation), position);
        return zone.AddNew(BarrierClass, handle, properties, low, high);
    }

    static (Vector3 Min, Vector3 Max) ShapeBounds(ChunkShape shape)
    {
        var points = ShapeVertices(shape).ToList();
        if (points.Count == 0) return (Vector3.Zero, Vector3.Zero);
        return (points.Aggregate(Vector3.Min), points.Aggregate(Vector3.Max));
    }

    static IEnumerable<Vector3> ShapeVertices(ChunkShape shape) => shape switch
    {
        ConvexShape convex => convex.Vertices,
        ShapeList list => list.Children.SelectMany(ShapeVertices),
        _ => [],
    };

    /// <summary>A shape moved: every point p to <c>Vector3.Transform(p, m)</c> (m turns and moves only).</summary>
    static ChunkShape Moved(ChunkShape shape, Matrix4x4 m) => shape switch
    {
        ConvexShape convex => new ConvexShape(convex.Radius, convex.Vertices.Select(v => Vector3.Transform(v, m)).ToArray(),
            convex.Planes.Select(p =>
            {
                var n = Vector3.TransformNormal(new Vector3(p.X, p.Y, p.Z), m);
                return new Vector4(n, p.W - Vector3.Dot(n, m.Translation));
            }).ToArray()),
        ShapeList list => new ShapeList(list.Children.Select(c => Moved(c, m)).ToList()),
        _ => shape,
    };

    /// <summary>A box (from <paramref name="min"/> to <paramref name="max"/>) as a convex shape.</summary>
    static ConvexShape BoxShape(Vector3 min, Vector3 max)
    {
        var corners = new Vector3[8];
        for (int k = 0; k < 8; k++) corners[k] = new Vector3((k & 1) == 0 ? min.X : max.X, (k & 2) == 0 ? min.Y : max.Y, (k & 4) == 0 ? min.Z : max.Z);
        Vector4[] planes =
        [
            new(1, 0, 0, -max.X), new(-1, 0, 0, min.X),
            new(0, 1, 0, -max.Y), new(0, -1, 0, min.Y),
            new(0, 0, 1, -max.Z), new(0, 0, -1, min.Z),
        ];
        return new ConvexShape(0, corners, planes);
    }

    /// <summary>
    /// A wall's shape as it is now, from the zone's middle (as the terrain file keeps it): one of the
    /// game's own moved and turned as its object was since it was read; a new one a box as its "bb".
    /// </summary>
    ChunkShape BarrierShapeNow(ZoneObject obj)
    {
        (Vector3 at, Matrix4x4 turn) = obj.Placement;
        if (barrierShapes.TryGetValue(obj.Handle, out var read))
            return Moved(read.Shape, Matrix4x4.CreateTranslation(barrierOrigin - read.Centre) * turn * Matrix4x4.CreateTranslation(at - barrierOrigin));
        float[] bb = obj.Property("bb") is { Size: 24 } box ? box.Floats() : [NewWallMin.X, NewWallMin.Y, NewWallMin.Z, NewWallMax.X, NewWallMax.Y, NewWallMax.Z];
        var shape = BoxShape(new Vector3(bb[0], bb[1], bb[2]), new Vector3(bb[3], bb[4], bb[5]));
        return Moved(shape, turn * Matrix4x4.CreateTranslation(at - barrierOrigin));
    }

    /// <summary>The walls as they are, to tell whether they changed.</summary>
    string BarrierSignature() => string.Join("\n", zone.Objects.Where(IsBarrier).Select(o =>
    {
        (Vector3 p, Matrix4x4 r) = o.Placement;
        string bb = o.Property("bb") is { Size: 24 } box ? string.Join(",", box.Floats().Select(v => v.ToString("R"))) : "";
        return $"{o.Handle:X8};{string.Join(",", new[] { p.X, p.Y, p.Z }.Concat(RotationOf(r)).Select(v => v.ToString("R")))};{bb}";
    }));

    /// <summary>Whether the walls changed since they were read or saved.</summary>
    bool BarriersChanged => barriersLoaded && barrierFile != null && BarrierSignature() != barrierSignature;

    /// <summary>
    /// Writes the walls into the zone's terrain file (every copy), if they changed: a record and a shape
    /// each (TerrainBarriers.Parts), their number in the file's terrain data (+0x94). The file is read
    /// as it is now (with ground sculpted and saved just before, if so). Returns what was done, for the log.
    /// </summary>
    string SaveBarriers()
    {
        if (!BarriersChanged || barrierFile == null) return "";
        if (context.Assets.ReadFile(barrierFile, archive, container) is not { } file) return $" Invisible walls: {barrierFile} couldn't be read, so they weren't saved.";
        try
        {
            TerrainFile terrain = TerrainFile.Read(file.Data);
            if (!terrain.Write().AsSpan().SequenceEqual(file.Data))
                return $" Invisible walls: {barrierFile} doesn't write back as it was read, so they weren't saved (nothing was changed).";
            var shapes = zone.Objects.Where(IsBarrier).Select(BarrierShapeNow).ToList();
            (terrain.BarrierRecords, terrain.BarrierSection) = TerrainBarriers.Parts(shapes);
            BitConverter.TryWriteBytes(terrain.Data.AsSpan(0x94), shapes.Count);
            string written = PutTerrainFiles(new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { [barrierFile] = terrain.Write() });
            barrierSignature = BarrierSignature();
            return $" Invisible walls: {shapes.Count} into {written}.";
        }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or NotSupportedException or ArgumentException or IndexOutOfRangeException or InvalidOperationException)
        {
            return $" Invisible walls couldn't be saved: {ex.Message}.";
        }
    }

    /// <summary>Adds a new invisible wall where the view points (a box 10 m wide, 10 m high, 1 m thick, standing on that point), and picks it.</summary>
    void AddBarrier(Vector3 at)
    {
        if (barrierFile == null)
        {
            MessageBox.Show($"{zoneName} has no terrain file to keep invisible walls in.", "Level editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        CommitEdit();
        var taken = zone.Objects.Select(o => o.Handle).ToHashSet();
        uint handle = BarrierHandles;
        while (taken.Contains(handle)) handle++;
        ZoneObject obj = NewBarrierObject(handle, at, Matrix4x4.Identity, NewWallMin, NewWallMax);
        scene?.Add(obj);
        RemovedObjects? removed = null;
        Record(new Change(obj, "Add",
            Undo: () => { removed = zone.Remove(obj); scene?.Remove(obj); },
            Redo: () => { if (removed != null) zone.PutBack(removed); scene?.Add(obj); },
            AddsOrRemoves: true));
        ObjectsChanged();
        PickInOutliner(obj);
        RefreshHelpers();
        context.Log($"Add: an invisible wall where the view points, at {at.X:0.#}, {at.Y:0.#}, {at.Z:0.#} (10 m wide, 10 m high, 1 m thick: change its bb to resize it). " +
                    "It goes into the terrain file on saving; the game has it once the level is loaded again.");
    }

    /// <summary>A wall's faces (each a fan round its corners on its plane), in the world, into a mesh of the helpers.</summary>
    void AddBarrierFaces(MeshGeometry3D mesh, ChunkShape shape)
    {
        switch (shape)
        {
            case ShapeList list:
                foreach (ChunkShape child in list.Children) AddBarrierFaces(mesh, child);
                break;
            case ConvexShape convex:
                foreach (Vector4 plane in convex.Planes)
                {
                    var normal = new Vector3(plane.X, plane.Y, plane.Z);
                    if (normal.LengthSquared() < 1e-6f) continue;
                    var onIt = convex.Vertices.Where(v => MathF.Abs(Vector3.Dot(normal, v) + plane.W) < 0.02f + convex.Radius).ToList();
                    if (onIt.Count < 3) continue;
                    Vector3 middle = onIt.Aggregate(Vector3.Zero, (a, b) => a + b) / onIt.Count;
                    Vector3 across = Vector3.Normalize(MathF.Abs(normal.Y) < 0.9f ? Vector3.Cross(normal, Vector3.UnitY) : Vector3.Cross(normal, Vector3.UnitX));
                    Vector3 along = Vector3.Cross(normal, across);
                    var round = onIt.OrderBy(v => MathF.Atan2(Vector3.Dot(v - middle, along), Vector3.Dot(v - middle, across))).ToList();
                    int start = mesh.Positions.Count;
                    foreach (Vector3 v in round) mesh.Positions.Add(ToView(barrierOrigin + v));
                    for (int i = 1; i + 1 < round.Count; i++)
                    {
                        mesh.TriangleIndices.Add(start);
                        mesh.TriangleIndices.Add(start + i);
                        mesh.TriangleIndices.Add(start + i + 1);
                    }
                }
                break;
        }
    }
}
