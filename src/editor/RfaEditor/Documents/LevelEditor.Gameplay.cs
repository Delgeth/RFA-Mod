using System.ComponentModel;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace RfaEditor;

// The Guerrilla port's gameplay markers in a Guerrilla world zone (see GameplayMarkers): its activity
// and mission nodes, raid nodes, kill, restricted and ambush regions, action nodes, kiosks, respawns,
// ... which the Guerrilla builder writes for the mods instead of into the zones. Shown read only:
// drawn in the 3D view (posts for places, see-through boxes for regions, walls for restricted areas),
// listed in the Scene panel by kind with eyes, and their details in the Inspector when picked there.
public partial class LevelEditor
{
    readonly ModelVisual3D gameplayContent = new();
    List<GameplayMarker> gameplayMarkers = [];
    readonly HashSet<string> hiddenGameplayKinds = new(StringComparer.Ordinal);
    readonly StackPanel markerDetails = new() { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 12, 0, 0) };
    GameplayMarker? pickedMarker;

    /// <summary>Sets the markers' layer up (called once, as the tab is made); they are read once the zone is drawn.</summary>
    void UseGameplayMarkers()
    {
        SceneViewport.Children.Add(gameplayContent);  // (the Direct3D view gathers this view's contents too)
        InspectorPart.Children.Insert(1, markerDetails);
        ObjectTree.SelectedItemChanged += OnMarkerRowChosen;
        // Anything else picked (an object, or nothing) puts the Inspector's title back: the marker's details go.
        DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock))
            .AddValueChanged(ObjectTitle, (_, _) => { if (pickedMarker != null && ObjectTitle.Text != pickedMarker.Label) HideMarker(); });
        Dispatcher.InvokeAsync(ReadGameplayMarkers, DispatcherPriority.Background);
    }

    /// <summary>Whether this is a zone of the Guerrilla world (on its 511 m grid by name), where the markers are.</summary>
    bool IsGuerrillaWorldZone => zoneName.Contains("rfg_", StringComparison.OrdinalIgnoreCase) && ZoneSquare() is { } square && MathF.Abs(square.Width - GuerrillaZone) < 0.5f;

    /// <summary>Reads the markers in this zone's square from the mods' files, and shows them.</summary>
    void ReadGameplayMarkers()
    {
        gameplayMarkers = [];
        if (IsGuerrillaWorldZone)
        {
            var (all, unread) = GameplayMarkers.All(Workspace.ModFolder);
            gameplayMarkers = all.Where(m => InZone(m.Position)).ToList();
            if (gameplayMarkers.Count > 0)
                context.Log($"{zoneName}: {gameplayMarkers.Count} of the Guerrilla port's gameplay markers (activities, regions, action nodes, ...) from the mods' files, " +
                            "shown read only" + (unread > 0 ? $"; {unread} lines of those files couldn't be read" : "") + ".");
        }
        DrawGameplayMarkers();
        BuildObjectTree(FilterBox.Text.Trim());
    }

    // ---- In the 3D view ------------------------------------------------------------------------

    void DrawGameplayMarkers()
    {
        if (gameplayMarkers.Count == 0)
        {
            gameplayContent.Content = null;
            return;
        }
        var (low, high) = WallHeights();
        var drawn = new Model3DGroup();
        foreach (GameplayMarker marker in gameplayMarkers.Where(m => !hiddenGameplayKinds.Contains(m.Kind)))
        {
            Color colour = GameplayMarkers.Style(marker.Family).Colour;
            if (marker.Polygon is { } polygon)
            {
                drawn.Children.Add(Walls(polygon, low, high, Look(colour, 60)));
                if (marker.OuterPolygon is { } outer) drawn.Children.Add(Walls(outer, low, high, Look(colour, 25)));
            }
            else if (marker.Box is var (min, max)) drawn.Children.Add(MarkerBox(marker, min, max, Look(colour, 50)));
            else drawn.Children.Add(MarkerBox(marker, new Vector3(-0.2f, 0, -0.2f), new Vector3(0.2f, 2.2f, 0.2f), Look(colour, 230)));  // a post, a person tall
        }
        drawn.Freeze();
        gameplayContent.Content = drawn;
    }

    /// <summary>
    /// How high restricted areas' walls stand (their polygons have no heights): from the ground's lowest
    /// to over its highest, or around the zone's other markers before the ground is drawn.
    /// </summary>
    (double Low, double High) WallHeights()
    {
        Rect3D ground = terrainContent.Content?.Bounds ?? Rect3D.Empty;
        if (!ground.IsEmpty) return (ground.Y, ground.Y + ground.SizeY + 20);
        var placed = gameplayMarkers.Where(m => m.Polygon == null).Select(m => (double)m.Position.Y).DefaultIfEmpty(0).ToList();
        return (placed.Min() - 20, placed.Max() + 60);
    }

    /// <summary>A colour as a material that also glows a little (markers show in the dark), see-through by its alpha (0-255).</summary>
    static Material Look(Color colour, byte alpha)
    {
        var look = new MaterialGroup();
        look.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromArgb(alpha, colour.R, colour.G, colour.B))));
        look.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromArgb((byte)(alpha / 2), colour.R, colour.G, colour.B))));
        look.Freeze();
        return look;
    }

    /// <summary>A box in a marker's own space (the game's: x right, y up, z forward), turned and placed as the marker is.</summary>
    static GeometryModel3D MarkerBox(GameplayMarker marker, Vector3 min, Vector3 max, Material look)
    {
        Vector3 size = Vector3.Max(max - min, new Vector3(0.05f)), middle = (min + max) / 2;
        var place = new Transform3DGroup();
        place.Children.Add(new ScaleTransform3D(size.X, size.Y, size.Z));
        place.Children.Add(new TranslateTransform3D(middle.X, middle.Y, -middle.Z));  // the view's Z is the game's -Z
        place.Children.Add(new MatrixTransform3D(LevelScene.ViewMatrix(marker.Position, marker.Turn)));
        return new GeometryModel3D(UnitCube, look) { BackMaterial = look, Transform = place };
    }

    /// <summary>See-through walls standing on a polygon of the map (x, z), from one height to another.</summary>
    static GeometryModel3D Walls(IReadOnlyList<Vector2> polygon, double low, double high, Material look)
    {
        var mesh = new MeshGeometry3D();
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i], b = polygon[(i + 1) % polygon.Count];
            int first = mesh.Positions.Count;
            mesh.Positions.Add(new Point3D(a.X, low, -a.Y));
            mesh.Positions.Add(new Point3D(b.X, low, -b.Y));
            mesh.Positions.Add(new Point3D(b.X, high, -b.Y));
            mesh.Positions.Add(new Point3D(a.X, high, -a.Y));
            foreach (int corner in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(first + corner);
        }
        mesh.Freeze();
        return new GeometryModel3D(mesh, look) { BackMaterial = look };
    }

    /// <summary>A 1 m cube around the origin.</summary>
    static readonly MeshGeometry3D UnitCube = MakeUnitCube();

    static MeshGeometry3D MakeUnitCube()
    {
        const double s = 0.5;
        var mesh = new MeshGeometry3D();
        Point3D[] corners = [new(-s, -s, -s), new(s, -s, -s), new(s, s, -s), new(-s, s, -s), new(-s, -s, s), new(s, -s, s), new(s, s, s), new(-s, s, s)];
        int[][] faces = [[0, 3, 2, 1], [4, 5, 6, 7], [0, 1, 5, 4], [2, 3, 7, 6], [1, 2, 6, 5], [0, 4, 7, 3]];
        foreach (int[] face in faces)
        {
            int first = mesh.Positions.Count;
            foreach (int corner in face) mesh.Positions.Add(corners[corner]);
            foreach (int index in new[] { 0, 1, 2, 0, 2, 3 }) mesh.TriangleIndices.Add(first + index);
        }
        mesh.Freeze();
        return mesh;
    }

    // ---- In the Scene panel and the Inspector ----------------------------------------------------

    /// <summary>The Scene panel's group of markers: by kind (each with an eye), each marker under its kind; narrowed by Find.</summary>
    TreeViewItem? GameplayGroup(string filter, bool finding)
    {
        var shown = gameplayMarkers.Where(m => filter.Length == 0 || m.Label.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                               m.Kind.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (shown.Count == 0) return null;
        var (groupColour, groupIcon) = GameplayMarkers.Style("Campaign");
        TreeViewItem group = Group("gameplay", groupIcon, new SolidColorBrush(groupColour), "Guerrilla's gameplay", shown.Count,
                                   "The Guerrilla port's activities, regions and nodes, from the mods' files (read only: the Guerrilla builder writes them)",
                                   eye: null, finding);
        foreach (var kind in shown.GroupBy(m => m.Kind).OrderBy(k => k.First().Family).ThenBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var (colour, icon) = GameplayMarkers.Style(kind.First().Family);
            string kindName = kind.Key;
            TreeViewItem row = Group("gameplay:" + kindName, icon, new SolidColorBrush(colour), kindName, kind.Count(), $"From {kind.First().File}",
                                     Eye(!hiddenGameplayKinds.Contains(kindName), on =>
                                     {
                                         if (on) hiddenGameplayKinds.Remove(kindName);
                                         else hiddenGameplayKinds.Add(kindName);
                                         DrawGameplayMarkers();
                                     }), finding);
            foreach (GameplayMarker marker in kind.OrderBy(m => m.Label, StringComparer.OrdinalIgnoreCase))
            {
                TreeViewItem leaf = Leaf(icon, Theme.Brush("DimTextBrush"), marker.Label, null, null, null);
                leaf.Tag = marker;
                row.Items.Add(leaf);
            }
            group.Items.Add(row);
        }
        return group;
    }

    void OnMarkerRowChosen(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if ((e.NewValue as TreeViewItem)?.Tag is GameplayMarker marker) ShowMarker(marker);
        else if (pickedMarker != null) HideMarker();
    }

    /// <summary>A marker picked in the Scene panel: its details in the Inspector, a box around it, and the view brought to it.</summary>
    void ShowMarker(GameplayMarker marker)
    {
        if (selected != null) Select(null, focus: false);
        pickedMarker = marker;
        var (colour, icon) = GameplayMarkers.Style(marker.Family);
        KindBadge.Visibility = Visibility.Visible;
        KindBadge.Background = new SolidColorBrush(Color.FromArgb(45, colour.R, colour.G, colour.B));
        KindIcon.Text = icon;
        KindIcon.Foreground = new SolidColorBrush(colour);
        ObjectTitle.Text = marker.Label;
        ObjectInfo.Text = $"{marker.Kind}, from {marker.File}";

        markerDetails.Children.Clear();
        void Row(string name, string value)
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) };
            text.Inlines.Add(new Run(name + "  ") { Foreground = Theme.Brush("DimTextBrush") });
            text.Inlines.Add(new Run(value));
            markerDetails.Children.Add(text);
        }
        if (marker.Handle is uint handle) Row("handle", handle.ToString("X8"));
        if (marker.Polygon == null) Row("place", $"{marker.Position.X:0.##}, {marker.Position.Y:0.##}, {marker.Position.Z:0.##}");
        if (marker.Box is var (min, max)) Row("box", $"{max.X - min.X:0.#} x {max.Y - min.Y:0.#} x {max.Z - min.Z:0.#} m");
        foreach (var (name, value) in marker.Properties) Row(name, value);
        markerDetails.Children.Add(new TextBlock
        {
            Text = "Read only for now: the Guerrilla builder writes this file anew on every build, so changes made here would be lost.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Foreground = Theme.Brush("DimTextBrush"), Margin = new Thickness(0, 10, 0, 0),
        });
        markerDetails.Visibility = Visibility.Visible;

        // A box around it, and the view brought to it.
        var (low, high) = WallHeights();
        Model3D around = marker.Polygon is { } polygon
            ? Walls(polygon, low, high, Look(Color.FromRgb(255, 220, 0), 70))
            : marker.Box is var (least, most)
                ? MarkerBox(marker, least - new Vector3(0.3f), most + new Vector3(0.3f), Look(Color.FromRgb(255, 220, 0), 70))
                : MarkerBox(marker, new Vector3(-0.6f, -0.2f, -0.6f), new Vector3(0.6f, 2.6f, 0.6f), Look(Color.FromRgb(255, 220, 0), 70));
        selectionBox.Content = around;
        camera.Frame(around.Bounds);
        UpdateCamera();
    }

    void HideMarker()
    {
        pickedMarker = null;
        markerDetails.Visibility = Visibility.Collapsed;
        markerDetails.Children.Clear();
        if (selected == null) selectionBox.Content = null;
    }
}
