using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AvalonDock;
using AvalonDock.Layout;

namespace RfaEditor;

/// <summary>
/// The window's layout: what you open (the preview of the picked file, tables, zones) as tabs in the
/// middle, and around them the panels: the Explorer, the Scene (a zone's objects and parts, or a
/// table's entries) and the Inspector of the tab you are working in, and the Log. Every panel and tab can be dragged by its title
/// to dock it elsewhere, made a tab of another, floated as a window of its own or pinned to a side,
/// and where you leave them is remembered (see <see cref="DockLayout"/>).
///
/// To add a panel: add it to <see cref="panels"/> in the constructor, and to the XAML if it should
/// start somewhere other than the bottom. A document gives a panel its content through
/// <see cref="IDocumentParts"/>.
/// </summary>
public partial class MainPage : UserControl
{
    readonly EditorContext context;
    readonly Action<IEditorDocument?, string?> activeChanged;  // the window shows the tab's menus, place and status
    readonly Dictionary<string, (IEditorDocument Document, LayoutDocument Tab, string Where)> documents = new(StringComparer.OrdinalIgnoreCase);
    readonly ContentControl outlinerHost = new(), inspectorHost = new(), assetsHost = new();
    readonly List<ShellPanel> panels;
    readonly DockLayout layout;
    IEditorDocument? active;

    /// <param name="log">The window's log, which the Log panel shows.</param>
    /// <param name="explorer">The window's explorer of the game's files, for the Explorer panel.</param>
    /// <param name="activeChanged">Called with the tab you are working in (and where its file is) when that changes.</param>
    /// <param name="game">The Game panel's view (the running game, docked), if the window has one: it starts as a tab in the middle.</param>
    internal MainPage(EditorContext context, ListBox log, FrameworkElement explorer, Action<IEditorDocument?, string?> activeChanged, FrameworkElement? game = null)
    {
        InitializeComponent();
        this.context = context;
        this.activeChanged = activeChanged;
        DockManager.Theme = new DockTheme();

        panels =
        [
            new ShellPanel("explorer", "Explorer", explorer),
            new ShellPanel("outliner", "Scene", OnPanel(outlinerHost)),
            new ShellPanel("inspector", "Inspector", OnPanel(new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = inspectorHost,
            })),
            new ShellPanel("log", "Log", LogView(log)),
            new ShellPanel("assets", "Assets", OnPanel(assetsHost), Beside: "log"),
        ];
        if (game != null) panels.Add(new ShellPanel("game", "Game", game));
        layout = new DockLayout(DockManager, "main", id => panels.FirstOrDefault(panel => panel.Id == id)?.View);
        layout.Restore();
        PlacePanels();

        DockManager.DocumentClosing += OnDocumentClosing;
        DockManager.DocumentClosed += OnDocumentClosed;
        ShowParts(null);
    }

    // ---- Panels --------------------------------------------------------------------------------

    /// <summary>
    /// Gives every panel its place in the layout, its title and its view. A panel the layout doesn't
    /// have (one newer than the saved layout, or one the XAML doesn't place) starts as a tab beside
    /// the panel it names (see <see cref="ShellPanel.Beside"/>), or else at the bottom.
    /// </summary>
    void PlacePanels()
    {
        List<LayoutAnchorable> placed = DockManager.Layout.Descendents().OfType<LayoutAnchorable>().ToList();
        foreach (ShellPanel panel in panels)
        {
            LayoutAnchorable? place = placed.FirstOrDefault(a => a.ContentId == panel.Id);
            bool isNew = place == null;
            place ??= new LayoutAnchorable { ContentId = panel.Id };
            place.Title = panel.Title;
            place.Content ??= panel.View;
            place.CanClose = false;  // a panel can't be lost: there is no way yet to bring one back
            place.CanHide = false;
            // The game's own window can't slide in and out with a panel pinned to a side.
            if (panel.Id == "game") place.CanAutoHide = false;
            if (!isNew) continue;
            if (panel.Id == "game")
            {
                DocumentPane().Children.Add(place);  // a tab with the documents to start with
                continue;
            }
            if (placed.FirstOrDefault(a => a.ContentId == panel.Beside)?.Parent is LayoutAnchorablePane beside) beside.Children.Add(place);
            else place.AddToLayout(DockManager, AnchorableShowStrategy.Bottom);
        }
    }

    /// <summary>A panel's content on the panels' background colour.</summary>
    static Border OnPanel(UIElement content) => new() { Background = Theme.Brush("PanelBrush"), Child = content };

    /// <summary>
    /// The log as the Log panel shows it: the same lines as the window's own log, each added here as
    /// it is added there, the newest scrolled into view.
    /// </summary>
    static ListBox LogView(ListBox windowLog)
    {
        var lines = new ObservableCollection<object>(windowLog.Items.Cast<object>());
        var list = new ListBox { FontFamily = new FontFamily("Consolas"), FontSize = 12, ItemsSource = lines, ItemTemplate = windowLog.ItemTemplate };
        ((INotifyCollectionChanged)windowLog.Items).CollectionChanged += (_, e) =>
        {
            if (e.NewItems == null) return;
            foreach (object line in e.NewItems) lines.Add(line);
            list.ScrollIntoView(lines[^1]);
        };
        return list;
    }

    // ---- Tabs ----------------------------------------------------------------------------------

    /// <summary>Adds something to work on as a tab in the middle, and brings it forward.</summary>
    /// <param name="where">Where its file is, shown in the window's title bar while it is the tab you work in.</param>
    internal void AddDocument(string key, IEditorDocument document, string where, bool closable = true)
    {
        var tab = new LayoutDocument { ContentId = key, Title = document.Title, Content = document.View, ToolTip = where, CanClose = closable, CanFloat = true };
        document.TitleChanged += () => tab.Title = document.Title;
        // The panels follow the tab you are working in, also when two are side by side.
        tab.IsSelectedChanged += (_, _) => { if (tab.IsSelected) ShowParts(document); };
        tab.IsActiveChanged += (_, _) => { if (tab.IsActive) ShowParts(document); };
        documents[key] = (document, tab, where);
        DocumentPane().Children.Add(tab);
        Select(tab);
        ShowParts(document);
    }

    /// <summary>Brings a tab forward, if it is open.</summary>
    internal void Show(string key)
    {
        if (documents.TryGetValue(key, out var open)) Select(open.Tab);
    }

    /// <summary>Opens a zone in the level editor, or brings its tab forward if it's open already.</summary>
    internal void OpenLevel(GameFile file)
    {
        string key = $"level:{file.Where}>{file.Name}";
        if (documents.ContainsKey(key))
        {
            Show(key);
            return;
        }
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            var editor = new LevelEditor(context, file.Archive, file.Container, file.Name);
            AddDocument(key, editor, file.Where);
            editor.UseDirect3DView();  // drawn on the graphics card from the start
        }
        catch (Exception ex)
        {
            context.Log($"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    /// <summary>Opens a table in the table editor, or brings its tab forward if it's open already.</summary>
    internal void OpenTable(GameFile file)
    {
        string key = "table:" + file.Name;
        if (documents.ContainsKey(key))
        {
            Show(key);
            return;
        }
        try
        {
            AddDocument(key, new TableEditor(context, file.Archive, file.Name), file.Where);
        }
        catch (Exception ex)
        {
            context.Log($"{ex.GetType().Name}: {ex.Message}");
        }
    }

    static void Select(LayoutDocument tab)
    {
        tab.IsSelected = true;
        tab.IsActive = true;
    }

    /// <summary>The pane the tabs are in. Panes come and go as tabs are dragged about, so it is looked up.</summary>
    LayoutDocumentPane DocumentPane()
    {
        LayoutDocumentPane? pane = documents.Values.Select(d => d.Tab.Parent).OfType<LayoutDocumentPane>().FirstOrDefault()
                                   ?? DockManager.Layout.Descendents().OfType<LayoutDocumentPane>().FirstOrDefault();
        if (pane == null)
        {
            pane = new LayoutDocumentPane();
            DockManager.Layout.RootPanel.Children.Insert(0, pane);
        }
        return pane;
    }

    /// <summary>Shows the parts of the tab you are working in (its scene or outliner, its inspector) in their panels.</summary>
    void ShowParts(IEditorDocument? document)
    {
        outlinerHost.Content = document?.Outliner ?? Hint("Open a zone or a table to see what is in it here.");
        inspectorHost.Content = document?.Inspector;
        assetsHost.Content = (document as IDocumentParts)?.PartFor("assets")
                             ?? Hint("Open a zone to add things to it from here: copies of its own objects, the game's chunks and level geometry, and new objects.");
        if (document == active && document != null) return;
        active = document;
        activeChanged(document, document == null ? null : documents.Values.FirstOrDefault(d => d.Document == document).Where);
    }

    static TextBlock Hint(string text) => new()
    {
        Text = text, Margin = new Thickness(8), TextWrapping = TextWrapping.Wrap, Foreground = Theme.Brush("DimTextBrush"),
    };

    /// <summary>A tab's close button: it asks to save first, and can stay open.</summary>
    void OnDocumentClosing(object? sender, DocumentClosingEventArgs e)
    {
        var open = documents.FirstOrDefault(d => d.Value.Tab == e.Document);
        if (open.Key != null && !open.Value.Document.ConfirmClose()) e.Cancel = true;
    }

    void OnDocumentClosed(object? sender, DocumentClosedEventArgs e)
    {
        string? key = documents.FirstOrDefault(d => d.Value.Tab == e.Document).Key;
        if (key != null) documents.Remove(key);
        ShowParts(documents.Values.FirstOrDefault(d => d.Tab.IsSelected).Document);
    }

    // ---- The game, docked ----------------------------------------------------------------------

    LayoutAnchorable? GamePlace() => DockManager.Layout.Descendents().OfType<LayoutAnchorable>().FirstOrDefault(a => a.ContentId == "game");

    /// <summary>Brings the Game panel forward.</summary>
    internal void ShowGame()
    {
        if (GamePlace() is not { } game) return;
        if (game.IsHidden) game.Show();
        game.IsSelected = true;
        game.IsActive = true;
    }

    /// <summary>Puts the Game panel where its Place button says (dragging it by its title does the same by hand).</summary>
    internal void PlaceGame(GamePanel.Place place)
    {
        if (GamePlace() is not { } game) return;
        switch (place)
        {
            case GamePanel.Place.Window:
                game.Float();
                return;
            case GamePanel.Place.Tab:
                game.Parent?.RemoveChild(game);
                DocumentPane().Children.Add(game);
                break;
            default:
                // A pane of its own, under or beside the documents: in the row or column the
                // documents are in if it runs that way, else in a new one that takes their place.
                Orientation way = place == GamePanel.Place.Below ? Orientation.Vertical : Orientation.Horizontal;
                game.Parent?.RemoveChild(game);
                var pane = new LayoutAnchorablePane(game);
                ILayoutElement middle = DocumentPane();
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
        game.IsSelected = true;
        game.IsActive = true;
    }

    // ---- Open tabs, for the window -------------------------------------------------------------

    /// <summary>The open tabs whose key starts with <paramref name="prefix"/> (e.g. a level archive's zones: "level:build\pc\cache\x.vpp_pc").</summary>
    internal List<(string Key, IEditorDocument Document)> OpenTabs(string prefix) =>
        documents.Where(d => d.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(d => (d.Key, d.Value.Document)).ToList();

    /// <summary>Closes a tab without asking to save (its file is being made again or deleted).</summary>
    internal void CloseTab(string key)
    {
        if (!documents.TryGetValue(key, out var open)) return;
        documents.Remove(key);
        open.Tab.Close();
        ShowParts(documents.Values.FirstOrDefault(d => d.Tab.IsSelected).Document);
    }

    // ---- The window closing --------------------------------------------------------------------

    /// <summary>Asks every tab with unsaved changes to save; false keeps the window open.</summary>
    internal bool ConfirmClose()
    {
        foreach (var (key, (document, tab, _)) in documents.ToList())
        {
            if (document.Title.StartsWith('*')) Select(tab);  // show which one the question is about
            if (!document.ConfirmClose()) return false;
            // It has agreed and let go of what it held, so its tab goes too, even if the window stays
            // open after all (another may still say no).
            documents.Remove(key);
            tab.Close();
        }
        return true;
    }

    /// <summary>The window is closing for certain: where the panels are is written down.</summary>
    internal void WindowClosing() => layout.Save();
}
