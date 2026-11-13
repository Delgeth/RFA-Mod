using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using RfaFormats.Zones;

namespace RfaEditor;

// The Scene panel: the zone as a tree, every row with an eye that draws it or not.
//   the zone            its objects by kind (models, lights, spawns, ...), each kind by class, each
//                       class its objects: an eye hides a whole kind, a whole class or one object;
//   its mission layers  each layer's objects by class, the layer's eye hiding all of it;
//   the level's parts   the zones around, the ground, foliage, decals, level geometry, the links and
//                       the grid.
// Picking a row picks the object; picking an object in the view picks its row. The Find box at the
// top narrows it down. What is hidden is only how it is drawn: the zone isn't changed, and a hidden
// object can't be clicked in the view.
public partial class LevelEditor
{
    /// <summary>A row with an eye for something the view draws or not, as BuildKindChips lists them.</summary>
    sealed record ShowRow(string Name, Brush? Colour, int? Count, string Tip, bool On, Action<bool> Toggled, ObjectKind? Kind, RelatedFile? File);

    readonly List<ShowRow> showRows = [];
    readonly HashSet<ZoneObject> hiddenObjects = [];            // hidden one by one (or by class)
    readonly HashSet<string> openRows = new(StringComparer.Ordinal) { "zone" };  // the groups left open, kept as the tree is made again
    readonly Dictionary<ZoneObject, ToggleButton> objectEyes = [];

    void BuildSceneTree(string filter)
    {
        // Which groups were open, to open them again.
        foreach (TreeViewItem item in Groups(ObjectTree.Items))
            if (item.Tag is string key)
            {
                if (item.IsExpanded) openRows.Add(key);
                else openRows.Remove(key);
            }
        ObjectTree.Items.Clear();
        treeItems.Clear();
        objectEyes.Clear();
        bool finding = filter.Length > 0;

        // The zone: its objects by kind, then by class.
        var zoneItem = Group("zone", Icons.Zone, Theme.Brush("AccentBrush"), zoneName, zone.Objects.Count,
                             "This zone's objects, by kind and class", eye: null, finding);
        var classes = ClassesOf(zone.Objects, filter);
        foreach (ObjectKind kind in ObjectKind.All)
        {
            var ofKind = classes.Where(c => c.Kind == kind).ToList();
            if (ofKind.Count == 0) continue;
            bool on = !hiddenKinds.Contains(kind);
            var kindItem = Group("kind:" + kind.Name, kind.Icon, kind.Brush, kind.Name, ofKind.Sum(c => c.Objects.Count),
                                 kind.Description + " (an eye here hides them in the zone's other files too)",
                                 Eye(on, shown =>
                                 {
                                     if (shown) hiddenKinds.Remove(kind);
                                     else hiddenKinds.Add(kind);
                                     ShowKinds();
                                 }), finding);
            foreach (var (name, _, objects) in ofKind) kindItem.Items.Add(ClassItem("class:" + kind.Name + "/" + name, name, kind, objects, finding));
            zoneItem.Items.Add(kindItem);
        }
        ObjectTree.Items.Add(zoneItem);

        // The zone's other files drawn with it (its mission layers), each by class.
        foreach (RelatedFile file in RelatedFiles())
        {
            ShowRow? row = showRows.FirstOrDefault(r => r.File == file);
            var fileClasses = ClassesOf(file.Zone.Objects, filter);
            if (finding && fileClasses.Count == 0) continue;
            var fileItem = Group("file:" + file.Label, file.Icon, Theme.Brush("RelatedBrush"), file.Label, file.Zone.Objects.Count, file.Description,
                                 Eye(file.Shown, shown => row?.Toggled(shown)), finding);
            foreach (var (name, kind, objects) in fileClasses) fileItem.Items.Add(ClassItem($"file:{file.Label}/{name}", name, kind, objects, finding));
            ObjectTree.Items.Add(fileItem);
        }

        // The level's parts: what is drawn besides the objects.
        if (!finding)
            foreach (ShowRow row in showRows.Where(r => r.Kind == null && r.File == null))
                ObjectTree.Items.Add(Leaf(PartIcon(row.Name), row.Colour ?? Theme.Brush("DimTextBrush"), row.Name, row.Count, row.Tip, Eye(row.On, row.Toggled)));
        AssetsMayHaveChanged();  // (the Assets panel lists copies of the zone's objects)
    }

    /// <summary>A class of objects: its eye hides or shows all of them; under it, each object with an eye of its own.</summary>
    TreeViewItem ClassItem(string key, string name, ObjectKind kind, List<ZoneObject> objects, bool finding)
    {
        List<ToggleButton> eyes = [];
        var item = Group(key, kind.Icon, kind.Brush, name, objects.Count, $"Objects of the class {name}",
                         Eye(!objects.All(hiddenObjects.Contains), shown =>
                         {
                             foreach (ZoneObject obj in objects)
                                 if (shown) hiddenObjects.Remove(obj);
                                 else hiddenObjects.Add(obj);
                             foreach (ToggleButton eye in eyes) eye.IsChecked = shown;
                             ShowKinds();
                         }), finding);
        foreach (ZoneObject obj in objects)
        {
            ToggleButton eye = Eye(!hiddenObjects.Contains(obj), shown =>
            {
                if (shown) hiddenObjects.Remove(obj);
                else hiddenObjects.Add(obj);
                ShowKinds();
            });
            eyes.Add(eye);
            objectEyes[obj] = eye;
            TreeViewItem row = Leaf(kind.Icon, Theme.Brush("DimTextBrush"), Label(obj), null, null, eye);
            row.Tag = obj;
            treeItems[obj] = row;
            item.Items.Add(row);
        }
        return item;
    }

    /// <summary>The classes of some objects that match the Find box, each with the kind most of its objects are, kinds in order.</summary>
    List<(string Name, ObjectKind Kind, List<ZoneObject> Objects)> ClassesOf(IEnumerable<ZoneObject> objects, string filter) =>
        objects.GroupBy(o => names.Class(o.ClassHash))
               .Select(g => (Name: g.Key, Kind: g.GroupBy(KindOf).MaxBy(k => k.Count())!.Key,
                             Objects: g.Where(o => filter.Length == 0 || g.Key.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                                   Label(o).Contains(filter, StringComparison.OrdinalIgnoreCase))
                                       .OrderBy(Label, StringComparer.OrdinalIgnoreCase).ToList()))
               .Where(c => c.Objects.Count > 0)
               .OrderBy(c => ObjectKind.All.IndexOf(c.Kind)).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
               .ToList();

    /// <summary>A row that holds others: open if it was, or while finding.</summary>
    TreeViewItem Group(string key, string icon, Brush colour, string name, int? count, string tip, ToggleButton? eye, bool finding)
    {
        TreeViewItem item = Leaf(icon, colour, name, count, tip, eye);
        item.Tag = key;
        item.IsExpanded = finding || openRows.Contains(key);
        return item;
    }

    /// <summary>A row: an icon, a name, how many it holds, and its eye at the right end.</summary>
    static TreeViewItem Leaf(string icon, Brush colour, string name, int? count, string? tip, ToggleButton? eye)
    {
        var header = new Grid { ToolTip = tip, Margin = new Thickness(0, 0, AssetBrowser.ScrollBarRoom, 0) };  // (the eyes clear of the scroll bar)
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        TextBlock iconText = Theme.Icon(icon, 13, colour);
        iconText.Width = 16;
        iconText.Margin = new Thickness(0, 0, 6, 0);
        var nameText = new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(nameText, 1);
        header.Children.Add(iconText);
        header.Children.Add(nameText);
        if (count != null)
        {
            var countText = new TextBlock { Text = count.Value.ToString("N0"), Foreground = Theme.Brush("DimTextBrush"), FontSize = 11,
                                            Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(countText, 2);
            header.Children.Add(countText);
        }
        if (eye != null)
        {
            Grid.SetColumn(eye, 3);
            header.Children.Add(eye);
            // What isn't drawn is dim in the list too.
            void Dim() => nameText.Opacity = iconText.Opacity = eye.IsChecked == true ? 1 : 0.45;
            eye.Checked += (_, _) => Dim();
            eye.Unchecked += (_, _) => Dim();
            Dim();
        }
        return new TreeViewItem { Header = header, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    }

    /// <summary>An eye: open while drawn. Clicking it doesn't pick the row.</summary>
    static ToggleButton Eye(bool on, Action<bool> toggled)
    {
        var eye = new ToggleButton { Style = Theme.Style("EyeToggle"), IsChecked = on };
        eye.Click += (_, _) => toggled(eye.IsChecked == true);
        return eye;
    }

    /// <summary>The icon of a part of the level, by the name of its row.</summary>
    static string PartIcon(string name) => name switch
    {
        "Zones around" => Icons.Zone,
        "Terrain" => Icons.Terrain,
        "Foliage" => Icons.Foliage,
        "Decals" => Icons.Texture,
        "Level geometry" => Icons.Pieces,
        "Links" => Icons.Link,
        "Grid" => Icons.Snap,
        _ => Icons.Marker,
    };

    /// <summary>Every row of a tree that holds others, at any depth.</summary>
    static IEnumerable<TreeViewItem> Groups(ItemCollection items)
    {
        foreach (TreeViewItem item in items.OfType<TreeViewItem>().Where(i => i.Tag is string))
        {
            yield return item;
            foreach (TreeViewItem inner in Groups(item.Items)) yield return inner;
        }
    }
}
