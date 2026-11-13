using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace RfaEditor;

/// <summary>Something the Assets panel lists, to add to the zone.</summary>
/// <param name="what">
/// What is added: for the level editor a zone object (a copy of it), a schema class (a new one of
/// it), a chunk or a piece of level geometry. The panel only shows it; its document adds it.
/// </param>
sealed class AssetItem(string icon, Brush colour, string name, string? detail, string note, string about, string tip, object what)
{
    public string Icon { get; } = icon;
    public Brush Colour { get; } = colour;
    public string Name { get; } = name;

    /// <summary>A second, dim name after the first (with the space before it).</summary>
    public string Detail { get; } = detail is { Length: > 0 } ? "  " + detail : "";

    /// <summary>A short note at the row's right end (how many, which pack).</summary>
    public string Note { get; } = note;

    /// <summary>A line or two under the preview.</summary>
    public string About { get; } = about;

    public string Tip { get; } = tip;
    public object What { get; } = what;
}

/// <summary>A folder on the Assets panel's left: what is in it directly, and its own folders.</summary>
/// <param name="note">Said when it has nothing to list (and why).</param>
sealed class AssetFolder(string name, string icon, Brush? colour, IReadOnlyList<AssetItem> items, IReadOnlyList<AssetFolder>? folders = null, string? note = null)
{
    public string Name { get; } = name;
    public string Icon { get; } = icon;
    public Brush? Colour { get; } = colour;
    public IReadOnlyList<AssetItem> Items { get; } = items;
    public IReadOnlyList<AssetFolder> Folders { get; } = folders ?? [];
    public string? Note { get; } = note;

    /// <summary>Everything in it and in its folders, each once.</summary>
    public List<AssetItem> All()
    {
        var all = new List<AssetItem>(Items);
        var seen = new HashSet<AssetItem>(Items);
        foreach (AssetFolder folder in Folders)
            foreach (AssetItem item in folder.All())
                if (seen.Add(item)) all.Add(item);
        return all;
    }
}

/// <summary>What the Assets panel shows things from, and adds them to: a document (the level editor).</summary>
interface IAssetSource
{
    /// <summary>The folders, made again each time they are asked for (what the document has may have changed).</summary>
    IReadOnlyList<AssetFolder> AssetFolders();

    /// <summary>A thing's model for the preview, or null to show its icon.</summary>
    Model3DGroup? AssetPreview(AssetItem item);

    /// <summary>Adds a thing where the middle of the view points (double-click, Enter).</summary>
    void AddAsset(AssetItem item);
}

/// <summary>
/// The Assets panel of a document: folders of things to add on the left, the chosen folder's
/// things in the middle (narrowed by Find), the picked one in 3D on the right. Double-click one
/// or press Enter to add it where the middle of the view points, or drag it into the view to put
/// it where you let go. What is dragged is the <see cref="AssetItem"/> itself
/// (<see cref="DragFormat"/>); the document's view takes the drop.
/// </summary>
public partial class AssetBrowser : UserControl
{
    /// <summary>The drag-and-drop format an <see cref="AssetItem"/> is carried in.</summary>
    internal static readonly string DragFormat = typeof(AssetItem).FullName!;

    /// <summary>Room left at the right end of a row for the scroll bar, which is drawn over the rows.</summary>
    internal const double ScrollBarRoom = 14;

    readonly IAssetSource source;
    readonly ModelViewer preview;
    readonly DispatcherTimer previewDelay = new() { Interval = TimeSpan.FromMilliseconds(120) };
    List<AssetItem> chosenItems = [];
    string chosenTitle = "";
    bool stale = true;
    bool refreshQueued;
    Point? pressedAt;
    AssetItem? pressedItem;

    internal AssetBrowser(IAssetSource source)
    {
        InitializeComponent();
        this.source = source;
        preview = new ModelViewer(PreviewViewport, PreviewArea);
        previewDelay.Tick += (_, _) => { previewDelay.Stop(); ShowPreview(); };
        IsVisibleChanged += (_, _) => { if (IsVisible && stale) Refresh(); };
        ShowPreview();
    }

    /// <summary>
    /// What the document has changed (objects added or deleted, the game's files read): the folders
    /// are made again, now if the panel is showing, else when it is next shown.
    /// </summary>
    internal void MarkStale()
    {
        stale = true;
        if (!IsVisible || refreshQueued) return;
        refreshQueued = true;
        Dispatcher.InvokeAsync(() => { refreshQueued = false; if (stale && IsVisible) Refresh(); }, DispatcherPriority.Background);
    }

    // ---- Folders -------------------------------------------------------------------------------

    /// <summary>A folder's row's tag: the folder, and its place (names joined by /; the root's is empty).</summary>
    sealed record FolderTag(AssetFolder Folder, string Key);

    /// <summary>Makes the folders again, keeping which are open, which is chosen and the picked thing.</summary>
    void Refresh()
    {
        using (HangWatch.While("making the Assets panel's lists")) RefreshNow();
    }

    void RefreshNow()
    {
        stale = false;
        IReadOnlyList<AssetFolder> folders = source.AssetFolders();
        // Which folders are open (at first, the top ones, so their ways of sorting show).
        var open = new HashSet<string>(Sources.Items.Count == 0 ? folders.Select(f => f.Name) : OpenFolders());
        string? chosen = (Sources.SelectedItem as TreeViewItem)?.Tag is FolderTag tag ? tag.Key : null;
        object? picked = (Items.SelectedItem as AssetItem)?.What;

        var everything = new AssetFolder("Everything", Icons.ShowAll, null, [], folders);
        Sources.Items.Clear();
        TreeViewItem root = FolderItem(everything, "", open, isRoot: true);
        Sources.Items.Add(root);
        foreach (AssetFolder folder in folders) Sources.Items.Add(FolderItem(folder, folder.Name, open));

        TreeViewItem? again = chosen == null ? null : Find(Sources.Items, chosen);
        (again ?? (TreeViewItem)Sources.Items[Math.Min(1, Sources.Items.Count - 1)]).IsSelected = true;  // (lists it: OnSourceChosen)
        if (picked != null && chosenItems.FirstOrDefault(i => i.What.Equals(picked)) is { } same && Items.Items.Contains(same))
        {
            Items.SelectedItem = same;
            Items.ScrollIntoView(same);
        }
    }

    /// <summary>A folder's row with its folders under it; its tag is the folder and its place (names joined by /).</summary>
    static TreeViewItem FolderItem(AssetFolder folder, string key, HashSet<string> open, bool isRoot = false)
    {
        int count = folder.All().Count;
        var header = new DockPanel();
        TextBlock icon = Theme.Icon(folder.Icon, 13, folder.Colour ?? Theme.Brush("DimTextBrush"));
        icon.Width = 18;
        icon.Margin = new Thickness(0, 0, 4, 0);
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon);
        var counted = new TextBlock { Text = count > 0 ? count.ToString("N0") : "", Foreground = Theme.Brush("DimTextBrush"), FontSize = 11,
                                      Margin = new Thickness(8, 0, ScrollBarRoom, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(counted, Dock.Right);
        header.Children.Add(counted);
        header.Children.Add(new TextBlock { Text = folder.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                                            Foreground = count == 0 ? Theme.Brush("DimTextBrush") : Theme.Brush("TextBrush") });
        var item = new TreeViewItem
        {
            Header = header, Tag = new FolderTag(folder, key), HorizontalContentAlignment = HorizontalAlignment.Stretch,
            IsExpanded = open.Contains(key), ToolTip = folder.Note,
        };
        if (!isRoot)
            foreach (AssetFolder inside in folder.Folders) item.Items.Add(FolderItem(inside, key + "/" + inside.Name, open));
        return item;
    }

    IEnumerable<string> OpenFolders()
    {
        IEnumerable<string> In(ItemCollection items) => items.OfType<TreeViewItem>()
            .SelectMany(i => (i.IsExpanded && i.Tag is FolderTag tag ? [tag.Key] : Array.Empty<string>()).Concat(In(i.Items)));
        return In(Sources.Items);
    }

    static TreeViewItem? Find(ItemCollection items, string key)
    {
        foreach (TreeViewItem item in items.OfType<TreeViewItem>())
        {
            if (item.Tag is FolderTag tag && tag.Key == key) return item;
            if (Find(item.Items, key) is { } inside)
            {
                item.IsExpanded = true;
                return inside;
            }
        }
        return null;
    }

    void OnSourceChosen(object sender, RoutedPropertyChangedEventArgs<object> e) => ListChosen();

    // ---- The list ------------------------------------------------------------------------------

    /// <summary>Lists what is in the chosen folder, narrowed by Find.</summary>
    void ListChosen()
    {
        if ((Sources.SelectedItem as TreeViewItem)?.Tag is not FolderTag(AssetFolder folder, string key))
        {
            chosenItems = [];
            chosenTitle = "";
        }
        else
        {
            chosenItems = folder.All();
            chosenTitle = key.Length == 0 ? "everything" : key.Replace("/", " › ");
            if (chosenItems.Count == 0 && folder.Note != null)
            {
                Items.ItemsSource = null;
                ListNote.Text = folder.Note;
                return;
            }
        }
        ApplyFind();
    }

    void OnFindChanged(object sender, TextChangedEventArgs e)
    {
        FindHint.Visibility = FindBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplyFind();
    }

    void ApplyFind()
    {
        string find = FindBox.Text.Trim();
        List<AssetItem> shown = find.Length == 0
            ? chosenItems
            : chosenItems.Where(i => i.Name.Contains(find, StringComparison.OrdinalIgnoreCase) || i.Detail.Contains(find, StringComparison.OrdinalIgnoreCase)).ToList();
        Items.ItemsSource = shown;
        ListNote.Text = find.Length == 0
            ? $"{shown.Count:N0} in {chosenTitle}. Double-click one to add it where the view points, or drag it into the view."
            : $"{shown.Count:N0} of {chosenItems.Count:N0} in {chosenTitle} have \"{find}\" in their name.";
    }

    void OnItemChosen(object sender, SelectionChangedEventArgs e)
    {
        previewDelay.Stop();
        previewDelay.Start();  // (a moment later, so stepping down the list with the keys stays quick)
    }

    AssetItem? ItemAt(object? hit) =>
        hit is DependencyObject element && ItemsControl.ContainerFromElement(Items, element) is ListBoxItem { DataContext: AssetItem item } ? item : null;

    void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemAt(e.OriginalSource) is not { } item) return;
        e.Handled = true;
        source.AddAsset(item);
    }

    void OnItemKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || Items.SelectedItem is not AssetItem item) return;
        e.Handled = true;
        source.AddAsset(item);
    }

    // ---- Dragging into the view ----------------------------------------------------------------

    void OnItemPressed(object sender, MouseButtonEventArgs e)
    {
        pressedItem = ItemAt(e.OriginalSource);
        pressedAt = pressedItem == null ? null : e.GetPosition(Items);
    }

    void OnItemDragged(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || pressedAt is not Point from || pressedItem is not { } item) return;
        Vector moved = e.GetPosition(Items) - from;
        if (Math.Abs(moved.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(moved.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        pressedAt = null;
        pressedItem = null;
        Items.SelectedItem = item;
        DragDrop.DoDragDrop(Items, new DataObject(DragFormat, item), DragDropEffects.Copy);
    }

    // ---- The preview ---------------------------------------------------------------------------

    void ShowPreview()
    {
        if (Items.SelectedItem is not AssetItem item)
        {
            preview.Clear();
            PreviewViewport.Visibility = Visibility.Collapsed;
            PreviewIcon.Visibility = Visibility.Collapsed;
            PreviewTitle.Text = "";
            PreviewInfo.Text = "Pick something to see it here.";
            return;
        }
        PreviewTitle.Text = item.Name;
        PreviewInfo.Text = item.About;
        Model3DGroup? model = null;
        Mouse.OverrideCursor = Cursors.Wait;
        var note = HangWatch.While($"reading {item.Name}'s model for the Assets panel's preview");
        try
        {
            model = source.AssetPreview(item);
        }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or NotSupportedException or InvalidOperationException)
        {
            PreviewInfo.Text = $"{item.About}\nIts model couldn't be read: {ex.Message}";
        }
        finally
        {
            Mouse.OverrideCursor = null;
            note.Dispose();
        }
        if (model != null)
        {
            PreviewIcon.Visibility = Visibility.Collapsed;
            PreviewViewport.Visibility = Visibility.Visible;
            preview.Show(model, frame: true);
        }
        else
        {
            preview.Clear();
            PreviewViewport.Visibility = Visibility.Collapsed;
            PreviewIcon.Text = item.Icon;
            PreviewIcon.Foreground = item.Colour;
            PreviewIcon.Visibility = Visibility.Visible;
        }
    }
}
