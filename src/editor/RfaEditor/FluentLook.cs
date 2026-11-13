using System.Windows;
using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;

namespace RfaEditor;

/// <summary>
/// The Fluent look (WPF UI), for the editor and the Guerrilla builder alike (the builder has this
/// file and Theme.xaml linked in): WPF UI's dark colours and its look for every control, then
/// Theme.xaml on top with the editor's own names for colours and styles, then the accent colour.
///
/// WPF UI reads two of Windows' settings from the registry: whether Windows is light or dark, and
/// its accent colour (accepted for this library; the editor's own code never touches the registry).
/// The editor uses neither: it is always dark, with its own accent.
/// </summary>
public static class FluentLook
{
    /// <summary>
    /// Puts the look into a program's resources. WPF UI's two dictionaries have to come first, at the
    /// top of the program's resources, for WPF UI to find them; Theme.xaml after them.
    /// </summary>
    /// <param name="theme">Where Theme.xaml is in the program, e.g. "pack://application:,,,/GuerrillaBuilder;component/Theme.xaml".</param>
    public static void Load(Application app, Uri theme)
    {
        app.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
        app.Resources.MergedDictionaries.Add(new ControlsDictionary());
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = theme });
        UseEditorAccent();
    }

    /// <summary>
    /// Gives WPF UI's controls the editor's accent colour (Fluent's blue, in its shades for a dark
    /// look), the one Theme.xaml's AccentBrush is, rather than the one chosen for Windows: a grey or
    /// dark accent there would hide the chosen tool and the selection. WPF UI is told not to look the
    /// Windows one up.
    /// </summary>
    public static void UseEditorAccent()
    {
        try
        {
            Wpf.Ui.UiApplication.Current.ApplySystemAccentColor = false;
            ApplicationAccentColorManager.Apply(
                systemAccent: Color.FromRgb(0x00, 0x78, 0xD4),
                primaryAccent: Color.FromRgb(0x4C, 0xC2, 0xFF),
                secondaryAccent: Color.FromRgb(0x60, 0xCD, 0xFF),  // = AccentBrush: what is filled with the accent
                tertiaryAccent: Color.FromRgb(0x99, 0xEB, 0xFF));
        }
        catch (Exception)
        {
            // WPF UI's own accent stays: a look a little off, nothing that stops the program.
        }
    }
}
