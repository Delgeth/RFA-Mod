using System.Windows;

namespace RfaEditor;

// The Game panel (the running game, docked: see Documents\GamePanel.cs) in the window's layout, and
// the window's way to the tabs it has open, wherever they are: the layout's (Shell\MainPage) or, if
// that couldn't be made, the old window's own.
public partial class MainWindow
{
    /// <summary>Puts the Game panel where its Place button says.</summary>
    void PlaceGame(GamePanel.Place place)
    {
        if (page == null)
        {
            Log("The Game panel needs the window's layout of panels, which couldn't be made.");
            return;
        }
        Try(() => page.PlaceGame(place));
    }

    /// <summary>Brings the Game panel forward.</summary>
    void OnShowGame(object sender, RoutedEventArgs e) => page?.ShowGame();

    /// <summary>The open tabs whose key starts with <paramref name="prefix"/>.</summary>
    List<(string Key, IEditorDocument Document)> OpenTabs(string prefix) =>
        page?.OpenTabs(prefix)
        ?? documents.Where(d => d.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).Select(d => (d.Key, d.Value.Document)).ToList();

    /// <summary>Closes a tab without asking to save (its file is being made again or deleted).</summary>
    void CloseTab(string key)
    {
        if (page != null)
        {
            page.CloseTab(key);
            return;
        }
        if (!documents.TryGetValue(key, out var open)) return;
        Documents.Items.Remove(open.Tab);
        documents.Remove(key);
    }
}
