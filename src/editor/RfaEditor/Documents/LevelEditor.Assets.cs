using System.Numerics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RfaFormats.Zones;
using Point = System.Windows.Point;

namespace RfaEditor;

// The zone's Assets panel (see AssetBrowser): what can be added to the zone, in folders (copies of
// this file's own objects, the game's chunks, level geometry, new objects from the game's schema),
// each shown in 3D when picked, and added where the middle of the view points (double-click) or
// where it is dropped in the view. The Add button's list (LevelEditor.xaml.cs and
// LevelEditor.ChunkBrowser.cs) offers the same things, found by typing.
public partial class LevelEditor : IAssetSource
{
    AssetBrowser? assetBrowser;
    ObjectModels? previewModels;
    int objectsListed = -1;  // how many objects the zone had when the panel last listed them

    /// <summary>The Assets panel, made the first time the window asks for it.</summary>
    AssetBrowser AssetsPanel()
    {
        if (assetBrowser != null) return assetBrowser;
        assetBrowser = new AssetBrowser(this);
        if (!context.Assets.IndexReady)
        {
            // The game's chunks are only known once the editor has read every container.
            void Ready()
            {
                context.Assets.IndexBecameReady -= Ready;
                Dispatcher.InvokeAsync(() => assetBrowser?.MarkStale());
            }
            context.Assets.IndexBecameReady += Ready;
        }
        return assetBrowser;
    }

    /// <summary>After the Scene panel was filled: if objects were added or deleted, the panel's copies are listed again.</summary>
    void AssetsMayHaveChanged()
    {
        if (assetBrowser == null || zone.Objects.Count == objectsListed) return;
        assetBrowser.MarkStale();
    }

    // ---- The folders ---------------------------------------------------------------------------

    List<AssetFolder>? gameFolders;  // the chunks, level geometry and new objects: the same once the editor has read the game's files

    IReadOnlyList<AssetFolder> IAssetSource.AssetFolders()
    {
        objectsListed = zone.Objects.Count;
        if (context.Assets.IndexReady) _ = context.Assets.ChunkPlacements();  // (looked for in the background, for adding a chunk)
        List<AssetFolder> game = gameFolders ?? [ChunksFolder(stitch: false), ChunksFolder(stitch: true), NewObjectsFolder()];
        if (context.Assets.IndexReady && context.Assets.ZoneSchema != null) gameFolders = game;
        return [ThisFileFolder(), .. game];
    }

    /// <summary>One of each kind of object this file has (by class and name), to add a copy of, by kind.</summary>
    AssetFolder ThisFileFolder()
    {
        var byKind = zone.Objects
            .GroupBy(o => (Class: names.Class(o.ClassHash), Label: Label(o).StartsWith('#') ? "" : Label(o)))
            .Select(g => (g.Key.Class, g.Key.Label, Example: g.First(), Count: g.Count(), Kind: KindOf(g.First())))
            .GroupBy(k => k.Kind)
            .OrderBy(g => ObjectKind.All.IndexOf(g.Key))
            .Select(g => new AssetFolder(g.Key.Name, g.Key.Icon, g.Key.Brush, g
                .OrderBy(k => k.Label.Length == 0 ? k.Class : k.Label, StringComparer.OrdinalIgnoreCase)
                .Select(k => new AssetItem(k.Kind.Icon, k.Kind.Brush, k.Label.Length > 0 ? k.Label : k.Class, k.Label.Length > 0 ? k.Class : null,
                                           k.Count.ToString("N0"),
                                           $"{k.Class}, {k.Count:N0} in this file. Adding one adds a copy, with what is joined to it.",
                                           $"{k.Class}: {k.Count:N0} in this file. Double-click to add a copy where the middle of the view points, or drag it into the view.",
                                           k.Example))
                .ToList()))
            .ToList();
        return new AssetFolder("This file", Icons.Zone, null, [], byKind, "Nothing is in this file yet.");
    }

    /// <summary>
    /// The game's chunks (buildings and props from every level and the Guerrilla world), or its
    /// stitch pieces (level geometry): by type (chunks), category, setting and pack.
    /// </summary>
    AssetFolder ChunksFolder(bool stitch)
    {
        string title = stitch ? "Level geometry" : "Chunks";
        ObjectKind kind = stitch ? ObjectKind.Geometry : ChunkKind;
        if (!context.Assets.IndexReady)
            return new AssetFolder(title, kind.Icon, kind.Brush, [], null, "These show here once the editor has read the game's files (a few seconds after it starts).");
        List<ChunkEntry> chunks = context.Assets.ChunkCatalog().Where(c => c.Stitch == stitch).ToList();
        var items = new Dictionary<string, AssetItem>(StringComparer.OrdinalIgnoreCase);
        foreach (ChunkEntry chunk in chunks) items.TryAdd(chunk.Name, ChunkItem(chunk, kind));

        var ways = new List<AssetFolder>();
        if (!stitch) ways.Add(new AssetFolder("By type", Icons.Folder, null, [], ChunkFolders(chunks, items, [ChunkFacet.Type, ChunkFacet.Category])));
        else ways.Add(new AssetFolder("By category", Icons.Folder, null, [], ChunkFolders(chunks, items, [ChunkFacet.Category])));
        ways.Add(new AssetFolder("By setting", Icons.Folder, null, [], ChunkFolders(chunks, items, [ChunkFacet.Setting])));
        ways.Add(new AssetFolder("By pack", Icons.Folder, null, [], ChunkFolders(chunks, items, [ChunkFacet.Pack])));
        return new AssetFolder(title, kind.Icon, kind.Brush, [], ways, "The game's files have none.");
    }

    /// <summary>A folder for each group of the first facet (Other last), split again by the next if that makes two groups or more.</summary>
    static List<AssetFolder> ChunkFolders(List<ChunkEntry> chunks, Dictionary<string, AssetItem> items, ChunkFacet[] facets)
    {
        ChunkFacet facet = facets[0];
        return chunks.SelectMany(c => c.Groups(facet).Select(group => (Group: group, Chunk: c)))
            .GroupBy(p => p.Group, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key is "Other" or "").ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                List<ChunkEntry> inGroup = g.Select(p => p.Chunk).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
                List<AssetFolder> inside = facets.Length > 1 ? ChunkFolders(inGroup, items, facets[1..]) : [];
                string name = g.Key.Length > 0 ? g.Key : "Other";
                return inside.Count >= 2
                    ? new AssetFolder(name, Icons.Folder, null, [], inside)
                    : new AssetFolder(name, Icons.Folder, null, inGroup.Select(c => items[c.Name]).ToList());
            })
            .ToList();
    }

    static AssetItem ChunkItem(ChunkEntry chunk, ObjectKind kind)
    {
        string packs = string.Join(", ", chunk.Places.Select(p => p.Container == null ? p.Pack : $"{p.Pack} › {p.Container}").Distinct().Take(6)) +
                       (chunk.Places.Count > 6 ? $" and {chunk.Places.Count - 6} more" : "");
        string about = $"{(chunk.Stitch ? "Level geometry" : chunk.Type)} · {chunk.Category} · {chunk.Setting}\nIn {packs}";
        string tip = chunk.Stitch
            ? $"{chunk.Name}: level geometry (a stitch piece), {chunk.Category.ToLowerInvariant()}, {chunk.Setting}.\nIn {packs}.\n" +
              "Added to the zone's stitch list; its model and textures go into the list's container when you save."
            : $"{chunk.Name}: {chunk.Type.ToLowerInvariant()}, {chunk.Category.ToLowerInvariant()}, {chunk.Setting}.\nIn {packs}.\n" +
              "Its model and textures go into this zone's container when you save.";
        return new AssetItem(kind.Icon, kind.Brush, chunk.Name, $"{chunk.Category} · {chunk.Setting}", chunk.Places.FirstOrDefault().Pack ?? "",
                             about, tip, chunk.Stitch ? new GeometryChoice(chunk.Name) : new ChunkChoice(chunk.Name));
    }

    /// <summary>Every class in the game's zone schema that can be made from it, to add a new one of, by kind.</summary>
    AssetFolder NewObjectsFolder()
    {
        if (context.Assets.ZoneSchema is not { } schema)
            return new AssetFolder("New objects", Icons.Add, null, [], null, "The game's zone schema hasn't been read (yet), so new objects can't be made.");
        var byKind = schema.Classes.Where(c => c.CantMake == null)
            .Select(c => (Class: c, Kind: ObjectKind.Of(c.ZoneClassNames[0], false), Title: c.Description is { Length: > 0 } title ? title : c.Name))
            .GroupBy(c => c.Kind)
            .OrderBy(g => ObjectKind.All.IndexOf(g.Key))
            .Select(g => new AssetFolder(g.Key.Name, g.Key.Icon, g.Key.Brush, g
                .OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase)
                .Select(c => new AssetItem(c.Kind.Icon, c.Kind.Brush, c.Title, c.Title != c.Class.Name ? c.Class.Name : null, c.Class.InTheGame ? "" : "unused",
                                           $"A new {c.Class.Name}, with every property the schema's default." +
                                           (c.Class.InTheGame ? "" : "\nNone of the game's levels have one, so the game may do nothing with it."),
                                           $"A new {c.Class.Name}{(c.Class.Description != null ? $" ({c.Class.Description})" : "")}, with every property the schema's default." +
                                           (c.Class.InTheGame ? "" : "\nNone of the game's levels have one, so the game may do nothing with it."),
                                           c.Class))
                .ToList()))
            .ToList();
        // The classes only Volition's tools could make: copies of the game's own (see LevelEditor.Examples.cs).
        Task<IReadOnlyDictionary<uint, List<ObjectExample>>> examples = context.Assets.ObjectExamples();
        if (examples.IsCompletedSuccessfully && examples.Result.Count > 0)
            byKind.Add(new AssetFolder("From the game's levels", Icons.Zone, null, examples.Result.Values.SelectMany(list => list)
                .OrderBy(e => e.Class).ThenBy(e => e.Label, StringComparer.OrdinalIgnoreCase)
                .Select(e =>
                {
                    ObjectKind kind = ObjectKind.Of(e.Class, false);
                    string about = $"A copy of {e.Place.Zone}'s {e.Class} ({e.Label}), with the objects of its set.";
                    return new AssetItem(kind.Icon, kind.Brush, $"{ClassTitle(e.Class)}: {e.Label}", e.Place.Zone, "", about,
                                         about + " Its data (script, points, bones) as the game's; every property editable.", e);
                }).ToList()));
        int cant = schema.Classes.Count(c => c.CantMake != null);
        return new AssetFolder("New objects", Icons.Add, null, [], byKind,
                               cant > 0 ? $"{cant} classes can't be made from the schema alone (the Add list says why)." : null);
    }

    // ---- The preview ---------------------------------------------------------------------------

    /// <summary>What a thing is drawn with in the zone, as one model in its own space (a turret's parts on one another); null if it has no model.</summary>
    Model3DGroup? IAssetSource.AssetPreview(AssetItem item)
    {
        IReadOnlyList<ObjectPart>? parts = item.What switch
        {
            ZoneObject obj => (previewModels ??= new ObjectModels(context.Assets, names, file => context.Assets.Model(file, archive, container))).Of(obj),
            ChunkChoice chunk => [new ObjectPart(chunk.Chunk + ".cchk_pc", null, Matrix4x4.Identity)],
            GeometryChoice piece => [new ObjectPart(piece.Piece + ".cstch_pc", null, Matrix4x4.Identity)],
            _ => null,
        };
        if (parts == null) return null;
        var model = new Model3DGroup();
        foreach (ObjectPart part in parts)
        {
            if (ModelParts(part.File, archive, container, uid: part.Uid) is not { } geometry) continue;
            var placed = part.Placement.IsIdentity ? model : new Model3DGroup { Transform = new MatrixTransform3D(LevelScene.ViewMatrix(part.Placement)) };
            foreach ((MeshGeometry3D mesh, Material material) in geometry)
                placed.Children.Add(new GeometryModel3D(mesh, material) { BackMaterial = material });
            if (placed != model) model.Children.Add(placed);
        }
        if (model.Children.Count == 0) return null;
        model.Freeze();
        return model;
    }

    // ---- Adding --------------------------------------------------------------------------------

    void IAssetSource.AddAsset(AssetItem item) => AddAssetAt(item, PointAhead());

    /// <summary>Adds a thing from the Assets panel at a place, as the Add list does.</summary>
    void AddAssetAt(AssetItem item, Vector3 at)
    {
        switch (item.What)
        {
            case ZoneObject example when !zone.Objects.Contains(example):
                context.Log($"Add: {item.Name} is no longer in this file (it was deleted after the Assets panel listed it).");
                assetBrowser?.MarkStale();
                break;
            case ZoneObject example:
                AddCopy(example, at, "Add");
                break;
            case SchemaClass type:
                if (CanPlaceAt(at, $"The new {type.Name}")) AddNew(type, at);
                break;
            case ChunkChoice chunk:
                if (CanPlaceAt(at, chunk.Chunk)) AddChunk(chunk.Chunk, at);
                break;
            case GeometryChoice piece:
                AddGeometry(piece.Piece, at);
                break;
            case ObjectExample example:
                AddExample(example, at);
                break;
        }
        ViewArea.Focus();
    }

    /// <summary>Lets the view take things dragged from the Assets panel: each goes where it is let go.</summary>
    void UseAssetDrops()
    {
        ViewArea.AllowDrop = true;
        ViewArea.DragEnter += OnAssetDragOver;
        ViewArea.DragOver += OnAssetDragOver;
        ViewArea.Drop += OnAssetDrop;
    }

    static void OnAssetDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(AssetBrowser.DragFormat) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    void OnAssetDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(AssetBrowser.DragFormat) is not AssetItem item) return;
        e.Handled = true;
        AddAssetAt(item, PointAt(e.GetPosition(ViewArea)));
    }

    /// <summary>Where a point of the view points: the first thing drawn there (within 300 m), or 15 m ahead along it.</summary>
    Vector3 PointAt(Point point)
    {
        RayMeshGeometry3DHitTestResult? first = HitsAt(point, sceneContent, relatedContent, geometryContent, terrainContent, decalContent, gridContent)
            .FirstOrDefault(hit => hit.DistanceToRayOrigin < 300);
        (Point3D origin, Vector3D direction) = camera.Ray(point, ViewArea.RenderSize);
        Point3D at = first?.PointHit ?? origin + direction * 15;
        return new Vector3((float)at.X, (float)at.Y, (float)-at.Z);
    }
}
