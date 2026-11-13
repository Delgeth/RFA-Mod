using System.Windows;
using AvalonDock.Themes;

namespace RfaEditor;

/// <summary>
/// The docking panels' look: AvalonDock's dark theme on the editor's Fluent colours (Shell\DockTheme.xaml).
/// </summary>
sealed class DockTheme : DictionaryTheme
{
    public DockTheme()
        : base(new ResourceDictionary { Source = new Uri("/RfaEditor;component/Shell/DockTheme.xaml", UriKind.Relative) })
    {
    }
}
