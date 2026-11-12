using System.Windows;

namespace RfaEditor;

/// <summary>
/// A panel of the window: something you can drag by its title to dock it elsewhere, make a tab of
/// another panel, float as a window of its own or pin to a side. Its id names it in the saved layout,
/// so it must stay the same from version to version.
///
/// To add a panel: add one of these to the list of panels in Shell\MainPage.xaml.cs. Where it starts
/// is in MainPage.xaml. A panel a saved layout doesn't have (one newer than it) starts as a tab
/// beside the panel named by <paramref name="Beside"/>, or else at the bottom.
/// </summary>
sealed record ShellPanel(string Id, string Title, FrameworkElement View, string? Beside = null);
