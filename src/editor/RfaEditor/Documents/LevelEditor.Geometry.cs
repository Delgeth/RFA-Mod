using System.Numerics;
using System.Text;
using System.Windows;
using RfaFormats;
using RfaFormats.Meshes;
using RfaFormats.Zones;

namespace RfaEditor;

// Level geometry (the stitch pieces the zone's stitch list places: rock walls, cliffs, ledges, roads,
// bridges) edited as objects: while a zone is open each piece is an object of the editor's own class
// "level_geometry" in it (its model, position and rotation; its list and flags), so everything that
// works on objects works on them: picking, the handles, the inspector's numbers, the controller,
// magnets, duplicating, deleting, undo. They are never saved in the zone file (taken out while it is
// written): saving writes them into the stitch list instead, every copy, with its decals kept and its
// collision tree made again to reach every piece.
public partial class LevelEditor
{
    /// <summary>A row of the Add list: a stitch piece to add as level geometry.</summary>
    sealed record GeometryChoice(string Piece);

    static readonly uint GeometryClass = NameHashes.ClassHash("level_geometry");
    const uint GeometryHandles = 0xFFFE0000;  // the pieces' handles while the zone is open (no zone uses these)

    static bool IsGeometry(ZoneObject obj) => obj.ClassHash == GeometryClass;

    bool geometryLoaded;                 // the stitch list's pieces are objects in the zone
    string? stitchListName;              // <zone>.stchlst_pc
    byte[]? stitchListRead;              // as read (its decals are written back from it)
    (string Archive, string? Container) stitchListAt;
    string geometrySignature = "";       // the pieces as read or last saved: whether they changed
    readonly Dictionary<string, ChunkPackage> stitchesToAdd = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The zone's stitch list's pieces as objects in it (once, before the scene is made).</summary>
    void EnsureGeometryObjects()
    {
        if (geometryLoaded) return;
        foreach (string listName in LevelGeometry.ZoneNames(zoneName).Select(z => z + ".stchlst_pc"))
        {
            if (context.Assets.ReadFile(listName, archive, container) is not { } found) continue;
            StitchList list;
            try { list = StitchList.Read(found.Data); }
            catch (Exception ex) when (ex is System.IO.InvalidDataException or NotSupportedException) { return; }
            stitchListName = listName;
            stitchListRead = found.Data;
            stitchListAt = (found.Archive, found.Container);
            uint handle = GeometryHandles;
            var taken = zone.Objects.Select(o => o.Handle).ToHashSet();
            foreach (StitchPiece piece in list.Pieces)
            {
                while (taken.Contains(handle)) handle++;
                NewGeometryObject(piece.Name, piece.List, piece.Flags, piece.Position, piece.Rotation, handle++);
            }
            geometryLoaded = true;
            geometrySignature = GeometrySignature();
            return;
        }
    }

    /// <summary>A stitch piece as an object in the zone (see the top), its box from its model placed.</summary>
    ZoneObject NewGeometryObject(string piece, int list, int flags, Vector3 position, Matrix4x4 rotation, uint handle)
    {
        byte[] Text(string text) => [.. Encoding.Latin1.GetBytes(text), 0];
        var placement = new byte[48];
        float[] values = [position.X, position.Y, position.Z, rotation.M11, rotation.M12, rotation.M13, rotation.M21, rotation.M22, rotation.M23, rotation.M31, rotation.M32, rotation.M33];
        for (int i = 0; i < values.Length; i++) BitConverter.TryWriteBytes(placement.AsSpan(i * 4), values[i]);
        var properties = new List<NewProperty>
        {
            new(ZoneProperty.TextType, NameHashes.PropertyHash("name"), Text(piece)),
            new(ZoneProperty.TextType, NameHashes.PropertyHash("chunk_name"), Text(piece + ".cstch_pc")),
            new(ZoneProperty.ValueType, NameHashes.PropertyHash("oP"), placement),
            new(ZoneProperty.ValueType, NameHashes.PropertyHash("stitch_list"), BitConverter.GetBytes(list)),
            new(ZoneProperty.ValueType, NameHashes.PropertyHash("stitch_flags"), BitConverter.GetBytes(flags)),
        };
        (Vector3 min, Vector3 max) = PieceBox(piece, position, rotation);
        return zone.AddNew(GeometryClass, handle, properties, min, max);
    }

    /// <summary>A stitch piece's box in the world, placed (from its model's; 2 m across if it can't be read).</summary>
    (Vector3 Min, Vector3 Max) PieceBox(string piece, Vector3 position, Matrix4x4 rotation)
    {
        (Vector3 min, Vector3 max) = context.Assets.Model(piece + ".cstch_pc", stitchListAt.Archive ?? archive, stitchListAt.Container) is ModelFile model
            ? ChunkBounds(model, null) : (new Vector3(-1), new Vector3(1));
        return StitchList.PlacedBox(min, max, RotationOf(rotation), position);
    }

    static float[] RotationOf(Matrix4x4 r) => [r.M11, r.M12, r.M13, r.M21, r.M22, r.M23, r.M31, r.M32, r.M33];

    static int IntOf(ZoneObject obj, string name, int otherwise) =>
        obj.Property(name) is { Size: 4 } p ? BitConverter.ToInt32(p.Value) : otherwise;

    static string PieceOf(ZoneObject obj) => obj.Property("name")?.Text ?? "";

    /// <summary>The pieces as they are, to tell whether they changed: each one's name, list, flags, position and rotation, in order.</summary>
    string GeometrySignature()
    {
        var text = new StringBuilder();
        foreach (ZoneObject obj in zone.Objects.Where(IsGeometry))
        {
            (Vector3 p, Matrix4x4 r) = obj.Placement;
            text.Append(PieceOf(obj)).Append(';').Append(ListOf(obj)).Append(';').Append(IntOf(obj, "stitch_flags", 4)).Append(';')
                .Append(string.Join(",", new[] { p.X, p.Y, p.Z }.Concat(RotationOf(r)).Select(v => v.ToString("R")))).Append('\n');
        }
        return text.ToString();
    }

    /// <summary>A piece's list: the sub-zone its position is in (column + 3 × row of the zone's thirds), where the zone's square is known; else the list it came from.</summary>
    int ListOf(ZoneObject obj)
    {
        if (ZoneSquare() is not { } square) return Math.Clamp(IntOf(obj, "stitch_list", 0), 0, StitchList.ListCount - 1);
        Vector3 p = obj.Placement.Position;
        float third = square.Width / 3;
        int column = Math.Clamp((int)MathF.Floor((p.X - square.Corner.X) / third), 0, 2), row = Math.Clamp((int)MathF.Floor((p.Z - square.Corner.Y) / third), 0, 2);
        return column + 3 * row;
    }

    /// <summary>Adds a stitch piece (from any of the game's levels) as level geometry where the view points, and picks it.</summary>
    void AddGeometry(string piece, Vector3 at)
    {
        if (!context.Assets.IndexReady)
        {
            context.Log("The editor is still reading the game's files (to find the piece). Try again in a few seconds.");
            return;
        }
        if (stitchListName == null)
        {
            MessageBox.Show($"{zoneName} has no stitch list to add level geometry to.", "Level editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        CommitEdit();
        var taken = zone.Objects.Select(o => o.Handle).ToHashSet();
        uint handle = GeometryHandles;
        while (taken.Contains(handle)) handle++;
        ZoneObject obj = NewGeometryObject(piece, 0, 4, at, Matrix4x4.Identity, handle);
        // Its files into the stitch list's containers that haven't them, on saving.
        string brought = "";
        if (!stitchesToAdd.ContainsKey(piece) && StitchContainers().Any(c => Workspace.Container(c.Archive, c.Container).Find(piece + ".cstch_pc") == null))
        {
            if (context.Assets.StitchPackageOf(piece, out string? why) is { } package)
            {
                stitchesToAdd[piece] = package;
                brought = $" Its {package.Files.Count} files ({package.Files.Sum(f => (long)f.Data.Length) / 1024:N0} KB) go into {string.Join(", ", StitchContainers().Select(c => c.Container))} on saving.";
            }
            else brought = $" Its files can't be brought in: {why}.";
        }
        scene?.Add(obj);
        RemovedObjects? removed = null;
        Record(new Change(obj, "Add",
            Undo: () => { removed = zone.Remove(obj); scene?.Remove(obj); },
            Redo: () => { if (removed != null) zone.PutBack(removed); scene?.Add(obj); },
            AddsOrRemoves: true));
        ObjectsChanged();
        PickInOutliner(obj);
        context.Log($"Add: {piece} as level geometry, where the view points, at {at.X:0.#}, {at.Y:0.#}, {at.Z:0.#}.{brought}");
    }

    /// <summary>The containers holding the zone's stitch list (where pieces brought in go).</summary>
    List<(string Archive, string Container)> StitchContainers() => stitchListName == null ? [] :
        context.Assets.PlacesOf(stitchListName).Where(p => p.Container != null).Select(p => (p.Archive, p.Container!)).Distinct().ToList();

    /// <summary>The zone's bytes without the level geometry (which goes in the stitch list).</summary>
    byte[] ZoneBytesWithoutGeometry()
    {
        var geometry = zone.Objects.Where(IsGeometry).ToList();
        if (geometry.Count == 0) return zone.ToBytes();
        RemovedObjects taken = zone.Remove(geometry);
        try { return zone.ToBytes(); }
        finally { zone.PutBack(taken); }
    }

    /// <summary>
    /// Writes the level geometry into the zone's stitch list (every copy), if it changed: the pieces,
    /// the list's decals as they were, a collision tree reaching every piece; and the files of
    /// pieces brought in from elsewhere into its containers. Returns what was done, for the log.
    /// </summary>
    string SaveGeometry()
    {
        if (!geometryLoaded || stitchListName == null || stitchListRead == null) return "";
        string signature = GeometrySignature();
        if (signature == geometrySignature && stitchesToAdd.Count == 0) return "";
        var said = new List<string>();
        // Pieces brought in from elsewhere (that some piece still names): into the list's containers.
        var named = zone.Objects.Where(IsGeometry).Select(PieceOf).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var bringing = stitchesToAdd.Values.Where(p => named.Contains(p.Chunk)).ToList();
        foreach (var to in StitchContainers())
        {
            var here = bringing.Where(p => Workspace.Container(to.Archive, to.Container).Find(p.Chunk + ".cstch_pc") == null).ToList();
            if (here.Count == 0) continue;
            said.Add($"brought {string.Join(", ", here.Select(p => p.Chunk))} into {to.Container}: " +
                     ContainerEdits.AddFiles(Workspace, to.Archive, to.Container, here.SelectMany(p => p.Files).ToList(),
                                             here.SelectMany(p => p.Primitives).ToList(), []).TrimEnd('.'));
        }
        if (signature != geometrySignature)
        {
            var objects = zone.Objects.Where(IsGeometry).ToList();
            var pieces = objects.Select(o =>
            {
                (Vector3 p, Matrix4x4 r) = o.Placement;
                return new StitchPieceToWrite(PieceOf(o), ListOf(o), p, RotationOf(r), IntOf(o, "stitch_flags", 4),
                                              new Vector2(o.BoundsMin.X, o.BoundsMin.Z), new Vector2(o.BoundsMax.X, o.BoundsMax.Z));
            }).ToList();
            var boxes = objects.Select(o => (o.BoundsMin, o.BoundsMax)).ToList();
            Vector3 middle = ZoneSquare() is { } square
                ? new Vector3(square.Corner.X + square.Width / 2, 0, square.Corner.Y + square.Width / 2)
                : boxes.Count > 0 ? new Vector3((boxes.Min(b => b.BoundsMin.X) + boxes.Max(b => b.BoundsMax.X)) / 2, 0, (boxes.Min(b => b.BoundsMin.Z) + boxes.Max(b => b.BoundsMax.Z)) / 2) : Vector3.Zero;
            byte[] list = StitchList.WriteEdited(stitchListRead, pieces, middle, boxes);
            foreach ((string inArchive, string? inContainer) in context.Assets.PlacesOf(stitchListName).Distinct())
                said.Add(PutZoneFile(stitchListName, inArchive, inContainer, list));
            said.Insert(0, $"{pieces.Count:N0} pieces of level geometry into {stitchListName}");
        }
        geometrySignature = signature;
        stitchesToAdd.Clear();
        return said.Count == 0 ? "" : " Level geometry: " + string.Join("; ", said) + ".";
    }

    /// <summary>Whether the zone's stitch list is overridden (or changed in one of our archives).</summary>
    bool StitchChanged() =>
        stitchListName != null && context.Assets.IndexReady &&
        context.Assets.PlacesOf(stitchListName).Any(p => Workspace.IsChanged(p.Archive, p.Container ?? stitchListName));

    /// <summary>Puts the zone's stitch list back as the game has it, in every place. Returns what was done ("" if nothing).</summary>
    string RestoreGeometry()
    {
        if (stitchListName == null) return "";
        var said = new List<string>();
        foreach ((string inArchive, string? inContainer) in context.Assets.PlacesOf(stitchListName).Distinct())
        {
            if (!Workspace.IsChanged(inArchive, inContainer ?? stitchListName)) continue;
            if (TryOriginal(inArchive, inContainer, stitchListName) is byte[] original)
                said.Add(PutZoneFile(stitchListName, inArchive, inContainer, original));
        }
        stitchesToAdd.Clear();
        geometryLoaded = false;  // read again (the zone is read again too)
        return said.Count == 0 ? "" : "Level geometry: " + string.Join("; ", said) + ".";
    }
}
