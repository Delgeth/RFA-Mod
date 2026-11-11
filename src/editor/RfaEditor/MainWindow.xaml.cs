using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AvalonDock;
using AvalonDock.Layout;
using RfaFormats;

namespace RfaEditor;

/// <summary>
/// The editor's window, laid out like other game editors (Nanoforge among them): the explorer of
/// the game's archives on the left, open documents as tabs in the middle (the preview of the picked
/// file, tables, levels), the chosen document's outliner and inspector on the right, and a log at
/// the bottom, with the running game beside it (the Game panel). Every panel can be dragged by its
/// title to dock it elsewhere, floated as a window of its own, or pinned to a side (AvalonDock). The
/// game folder is only ever read.
/// </summary>
public partial class MainWindow : Window
{
    readonly Workspace workspace;
    readonly EditorContext context;
    readonly FilePreview preview;
    readonly GamePanel gamePanel;
    readonly Dictionary<string, (IEditorDocument Document, LayoutDocument Tab)> documents = new(StringComparer.OrdinalIgnoreCase);
    List<IndexedFile> searchIndex = [];
    GameFile? selectedFile;  // the file picked in the explorer, if a file is

    internal MainWindow(Workspace workspace)
    {
        InitializeComponent();
        this.workspace = workspace;
        context = new EditorContext
        {
            Workspace = workspace,
            Assets = new GameAssets(workspace),
            Log = Log,
            OverridesChanged = RefreshOverrideMarks,
            OpenTable = OpenTable,
            OpenLevel = OpenLevel,
        };
        GameFolderText.Text = $"Game: {workspace.GameFolder}      Overrides: {workspace.OverridesFolder}";
        preview = new FilePreview(context);
        AddDocument("preview", preview, closable: false);
        BuildTree();
        _ = BuildSearchIndexAsync();

        DockManager.Theme = new AvalonDock.Themes.Vs2013DarkTheme();
        DockManager.DocumentClosing += OnDocumentClosing;
        DockManager.DocumentClosed += OnDocumentClosed;
        gamePanel = new GamePanel(context, PlaceGame);
        GameHostPanel.Content = gamePanel.View;
    }

    // ---- Log -----------------------------------------------------------------------------------

    void Log(string text)
    {
        LogList.Items.Add(new LogLine($"{DateTime.Now:HH:mm:ss}", text));
        EditorLog.Write($"{DateTime.Now:HH:mm:ss} {text}");
        LogList.ScrollIntoView(LogList.Items[^1]);
        StatusText.Text = text;
    }

    // ---- Documents -----------------------------------------------------------------------------

    void AddDocument(string key, IEditorDocument document, bool closable = true)
    {
        var tab = new LayoutDocument { ContentId = key, Title = document.Title, Content = document.View, CanClose = closable, CanFloat = true };
        document.TitleChanged += () => tab.Title = document.Title;
        tab.IsSelectedChanged += (_, _) =>
        {
            if (tab.IsSelected) ShowParts(document);
        };
        documents[key] = (document, tab);
        Documents().Children.Add(tab);
        Select(tab);
        ShowParts(document);
    }

    static void Select(LayoutDocument tab)
    {
        tab.IsSelected = true;
        tab.IsActive = true;
    }

    /// <summary>Asks the document to save first when its tab's close button is used.</summary>
    void OnDocumentClosing(object? sender, DocumentClosingEventArgs e)
    {
        var open = documents.FirstOrDefault(d => d.Value.Tab == e.Document);
        if (open.Key != null && !open.Value.Document.ConfirmClose()) e.Cancel = true;
    }

    void OnDocumentClosed(object? sender, DocumentClosedEventArgs e)
    {
        string? key = documents.FirstOrDefault(d => d.Value.Tab == e.Document).Key;
        if (key != null) documents.Remove(key);
        if (!documents.Values.Any(d => d.Tab.IsSelected)) ShowParts(null);
    }

    /// <summary>Shows the chosen document's outliner and inspector on the right.</summary>
    void ShowParts(IEditorDocument? document)
    {
        OutlinerHost.Content = document?.Outliner ?? Hint("Open a level or a table to see what is in it here.");
        InspectorHost.Content = document?.Inspector;
    }

    static TextBlock Hint(string text) => new()
    {
        Text = text, Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap,
        Foreground = (System.Windows.Media.Brush)Application.Current.Resources["DimTextBrush"],
    };

    void ShowPreview() => Select(documents["preview"].Tab);

    /// <summary>The pane the documents are tabs of (panels can be dragged about, so it is looked up).</summary>
    LayoutDocumentPane Documents() =>
        documents.Values.Select(d => d.Tab.Parent).OfType<LayoutDocumentPane>().FirstOrDefault()
        ?? DockManager.Layout.Descendents().OfType<LayoutDocumentPane>().FirstOrDefault()
        ?? DocumentPane;

    // ---- The game, docked ----------------------------------------------------------------------

    /// <summary>Puts the Game panel where its Place button says (dragging it by its title does the same by hand).</summary>
    void PlaceGame(GamePanel.Place place)
    {
        Try(() =>
        {
            switch (place)
            {
                case GamePanel.Place.Window:
                    GameAnchorable.Float();
                    return;
                case GamePanel.Place.Tab:
                    GameAnchorable.Parent?.RemoveChild(GameAnchorable);
                    Documents().Children.Add(GameAnchorable);
                    break;
                default:
                    // A pane of its own, under or beside the documents: in the row or column the
                    // documents are in if it runs that way, else in a new one that takes their place.
                    Orientation way = place == GamePanel.Place.Below ? Orientation.Vertical : Orientation.Horizontal;
                    GameAnchorable.Parent?.RemoveChild(GameAnchorable);
                    var pane = new LayoutAnchorablePane(GameAnchorable);
                    ILayoutElement middle = Documents();
                    while (middle.Parent is LayoutDocumentPaneGroup group) middle = group;
                    if (middle.Parent is LayoutPanel row && row.Orientation == way)
                    {
                        row.InsertChildAt(row.IndexOfChild(middle) + 1, pane);
                    }
                    else if (middle.Parent is ILayoutContainer container)
                    {
                        var split = new LayoutPanel { Orientation = way };
                        container.ReplaceChild(middle, split);
                        split.Children.Add((ILayoutPanelElement)middle);
                        split.Children.Add(pane);
                    }
                    break;
            }
            GameAnchorable.IsSelected = true;
            GameAnchorable.IsActive = true;
        });
    }

    /// <summary>Brings the Game panel forward.</summary>
    void OnShowGame(object sender, RoutedEventArgs e)
    {
        GameAnchorable.IsSelected = true;
        GameAnchorable.IsActive = true;
    }

    /// <summary>Opens a table in the table editor, or brings its tab forward if it's open already.</summary>
    void OpenTable(GameFile file)
    {
        string key = "table:" + file.Name;
        if (documents.TryGetValue(key, out var open))
        {
            Select(open.Tab);
            return;
        }
        Try(() => AddDocument(key, new TableEditor(context, file.Archive, file.Name)));
    }

    /// <summary>Opens a zone in the level editor, or brings its tab forward if it's open already.</summary>
    void OpenLevel(GameFile file)
    {
        string key = $"level:{file.Where}>{file.Name}";
        if (documents.TryGetValue(key, out var open))
        {
            Select(open.Tab);
            return;
        }
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            Try(() => AddDocument(key, new LevelEditor(context, file.Archive, file.Container, file.Name)));
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    void OnSourceInitialized(object? sender, EventArgs e) => DarkTitleBar.Apply(this);

    void OnClosing(object? sender, CancelEventArgs e)
    {
        foreach (var (document, tab) in documents.Values.ToList())
        {
            Select(tab);
            if (!document.ConfirmClose())
            {
                e.Cancel = true;
                return;
            }
        }
        gamePanel.Release();  // the game gets its own window back before the editor's goes
    }

    // ---- Explorer ------------------------------------------------------------------------------

    void BuildTree()
    {
        var folders = workspace.Archives()
            .GroupBy(archive => Path.GetDirectoryName(archive) ?? "")
            .Select(group => new FolderNode(group.Key, group.Select(archive => new ArchiveNode(workspace, archive))));
        Tree.ItemsSource = folders.ToList();
    }

    void OnTreeItemExpanded(object sender, RoutedEventArgs e)
    {
        if ((e.OriginalSource as TreeViewItem)?.DataContext is TreeNode node && !node.ChildrenLoaded)
            Try(() => node.LoadChildren());
    }

    void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        selectedFile = (e.NewValue as FileNode)?.File;
        switch (e.NewValue)
        {
            case FileNode file: preview.ShowFile(file.File, file); break;
            case ArchiveNode archive: preview.ShowArchive(archive); break;
            case FolderNode folder: preview.ShowFolder(folder); break;
            default: preview.ShowNothing(); break;
        }
        ShowPreview();
    }

    void OnFileDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (selectedFile != null) FilePreview.OpenInEditor(selectedFile, context);
    }

    async Task BuildSearchIndexAsync()
    {
        List<(string, string)> archives = workspace.Archives().Select(archive => (archive, workspace.ArchivePath(archive))).ToList();
        var progress = new Progress<string>(text => StatusText.Text = text);
        try
        {
            searchIndex = await Task.Run(() => Native.InBackground(() => FileIndex.ArchiveFiles(archives)));
            StatusText.Text = $"{searchIndex.Count:N0} files in the archives. Reading the containers for search, models and textures...";
            var textures = new Dictionary<string, TextureRef>(StringComparer.OrdinalIgnoreCase);
            // Reading every container takes a minute of disk work: done at background priority, so a
            // game starting meanwhile (the Game panel) gets the disk first and doesn't stall.
            List<IndexedFile> inContainers = await Task.Run(() => Native.InBackground(() => FileIndex.ContainerFiles(archives, progress, textures)));
            searchIndex = [.. searchIndex, .. inContainers];
            context.Assets.SetIndex(searchIndex, textures);
            Log($"Ready: {searchIndex.Count:N0} files can be searched.");
            if (SearchBox.Text.Length > 0) OnSearchChanged(SearchBox, null!);
        }
        catch (Exception ex)
        {
            Log("Reading the containers failed: " + ex.Message);
        }
    }

    void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        string text = SearchBox.Text.Trim();
        bool searching = text.Length > 0;
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Tree.Visibility = searching ? Visibility.Collapsed : Visibility.Visible;
        SearchResults.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        if (!searching) return;

        const int limit = 2000;
        var matches = searchIndex.Where(f => f.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(limit + 1).ToList();
        SearchResults.ItemsSource = matches.Take(limit).ToList();
        StatusText.Text = matches.Count > limit ? $"More than {limit} files match; showing the first {limit}." : $"{matches.Count} files match.";
    }

    void OnSearchSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SearchResults.SelectedItem is not IndexedFile found) return;
        Try(() =>
        {
            Packfile source = found.Container == null
                ? workspace.Archive(found.Archive)
                : workspace.Container(found.Archive, found.Container);
            PackfileEntry entry = source.Find(found.Name) ?? throw new FileNotFoundException(found.Name);
            selectedFile = new GameFile(source, entry, found.Archive, found.Container);
            preview.ShowFile(selectedFile, null);
            ShowPreview();
        });
    }

    // ---- Overrides -----------------------------------------------------------------------------

    /// <summary>Looks at overrides\ again (files may have been added or removed there) and updates
    /// the "changed" marks and the picked file's details.</summary>
    void RefreshOverrideMarks()
    {
        workspace.RefreshOverrides();
        foreach (TreeNode node in Tree.Items.OfType<TreeNode>().SelectMany(n => n.LoadedNodes()))
            (node as FileNode)?.UpdateChangedMark();
        preview.RefreshDetails();
    }

    void OnActivated(object? sender, EventArgs e) => RefreshOverrideMarks();

    void OnOpenOverridesFolder(object sender, RoutedEventArgs e)
    {
        Directory.CreateDirectory(workspace.OverridesFolder);
        Process.Start("explorer.exe", $"\"{workspace.OverridesFolder}\"");
    }

    void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Log($"{ex.GetType().Name}: {ex.Message}");
        }
    }
}

/// <summary>A line in the log panel: when, and what happened.</summary>
sealed record LogLine(string Time, string Text);
