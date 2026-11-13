using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using RfaFormats;

namespace GuerrillaBuilder;

/// <summary>
/// The builder's window. It ticks what isn't built (or needs building again) when it opens, so
/// after setting up or updating RFA-Mod the one thing to do is Build. The work runs on another
/// thread, its lines shown as they come.
/// </summary>
public partial class MainWindow : Window
{
    readonly Setup setup = Setup.Find();
    readonly Dictionary<Part, (CheckBox Box, TextBlock Status)> rows = [];
    bool working;

    static readonly Brush BuiltBrush = Frozen(Color.FromRgb(0x6C, 0xC0, 0x70));

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => RfaEditor.DarkTitleBar.Apply(this);
        foreach (var part in Parts.All) PartRows.Children.Add(Row(part));
        GameBox.Text = setup.Game ?? "";
        string? found = setup.Guerrilla == null ? setup.FindGuerrilla() : null;
        if (found != null) setup.UseGuerrilla(found);
        GuerrillaBox.Text = setup.Guerrilla ?? "";
        ShowFolders(found != null ? "Found in your Steam library; kept in rfa_mods.ini when you build." : null);
        ShowParts(tick: true);
        foreach (string line in Parts.TidyOldTools()) Append(line);
        Closing += OnClosing;
    }

    /// <summary>A part's row: a tick box holding its name, how it stands and what it is.</summary>
    UIElement Row(Part part)
    {
        var name = new TextBlock { Text = Parts.Name(part), FontWeight = FontWeights.SemiBold, FontSize = 14 };
        var status = new TextBlock { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Bottom, TextWrapping = TextWrapping.Wrap };
        var top = new DockPanel();
        top.Children.Add(name);
        top.Children.Add(status);
        var text = new StackPanel { Margin = new Thickness(4, 0, 0, 0) };
        text.Children.Add(top);
        text.Children.Add(new TextBlock
        {
            Text = Parts.Description(part), Foreground = Resource("DimTextBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0),
        });
        var box = new CheckBox { Content = text, Margin = new Thickness(0, 8, 0, 0), Padding = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center };
        rows[part] = (box, status);
        return box;
    }

    void ShowParts(bool tick)
    {
        foreach (var (part, (box, status)) in rows)
        {
            var now = Parts.Status(setup, part);
            status.Text = now.Text;
            status.Foreground = now.State switch
            {
                State.Built => BuiltBrush,
                State.BuildAgain => Resource("ChangedBrush"),
                _ => Resource("DimTextBrush"),
            };
            if (tick) box.IsChecked = now.State != State.Built;
        }
        if (!working)
            StatusText.Text = rows.Keys.All(p => Parts.Status(setup, p).State == State.Built)
                ? "Everything is built. Start the game with RFAModLauncher.exe; in the developer console (the ` key) type: level rfg_12_11"
                : "Tick what to build, then Build. What isn't built yet, or needs building again, is ticked already.";
    }

    void ShowFolders(string? guerrillaHint = null)
    {
        string? guerrillaProblem = setup.GuerrillaProblem, gameProblem = setup.GameProblem;
        GuerrillaNote.Text = guerrillaProblem ?? guerrillaHint ?? "Found.";
        GuerrillaNote.Foreground = guerrillaProblem != null ? Resource("ErrorBrush") : BuiltBrush;
        GameNote.Text = gameProblem ?? "Found.";
        GameNote.Foreground = gameProblem != null ? Resource("ErrorBrush") : BuiltBrush;
    }

    // ---- The folders: chosen with Browse or typed in, and kept in rfa_mods.ini.

    void OnBrowseGuerrilla(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Where is Red Faction: Guerrilla? Choose its folder (Red Faction Guerrilla in your Steam library)" };
        if (!string.IsNullOrEmpty(GuerrillaBox.Text) && Directory.Exists(GuerrillaBox.Text)) dialog.InitialDirectory = GuerrillaBox.Text;
        if (dialog.ShowDialog(this) != true) return;
        GuerrillaBox.Text = dialog.FolderName;
        KeepGuerrilla();
    }

    void OnBrowseGame(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = $"Where is Red Faction: Armageddon? Choose the folder with {GameFolder.ExeName}" };
        if (!string.IsNullOrEmpty(GameBox.Text) && Directory.Exists(GameBox.Text)) dialog.InitialDirectory = GameBox.Text;
        if (dialog.ShowDialog(this) != true) return;
        GameBox.Text = dialog.FolderName;
        KeepGame();
    }

    void OnGuerrillaTyped(object sender, RoutedEventArgs e)
    {
        if (GuerrillaBox.Text.Trim() != (setup.Guerrilla ?? "")) KeepGuerrilla();
    }

    void OnGameTyped(object sender, RoutedEventArgs e)
    {
        if (GameBox.Text.Trim() != (setup.Game ?? "")) KeepGame();
    }

    /// <summary>Uses the folder in the box, kept in rfa_mods.ini if it is Guerrilla's.</summary>
    void KeepGuerrilla()
    {
        setup.UseGuerrilla(GuerrillaBox.Text);
        if (setup.GuerrillaProblem == null) setup.KeepGuerrilla(GuerrillaBox.Text);
        ShowFolders();
    }

    void KeepGame()
    {
        if (!setup.KeepGame(GameBox.Text))
        {
            GameNote.Text = $"{GameBox.Text} isn't Red Faction: Armageddon's folder (it has no {GameFolder.ExeName}).";
            GameNote.Foreground = Resource("ErrorBrush");
            return;
        }
        ShowFolders();
        ShowParts(tick: false);
    }

    // ---- Building and taking away.

    List<Part> Ticked() => [.. rows.Where(r => r.Value.Box.IsChecked == true).Select(r => r.Key)];

    async void OnBuild(object sender, RoutedEventArgs e)
    {
        KeepGuerrilla();
        var parts = Ticked();
        if (parts.Count == 0)
        {
            StatusText.Text = "Tick what to build first.";
            return;
        }
        await Work("Building", log => Parts.Build(setup, parts, new Options(), log));
    }

    async void OnRemove(object sender, RoutedEventArgs e)
    {
        var parts = Ticked();
        if (parts.Count == 0)
        {
            StatusText.Text = "Tick what to take away first.";
            return;
        }
        var all = Parts.WithBuiltOn(parts);
        string list = string.Join(", ", all.Select(Parts.Noun));
        string also = all.Count > parts.Count ? $"\n\n({string.Join(", ", all.Except(parts).Select(Parts.Noun))} {(all.Count - parts.Count == 1 ? "goes" : "go")} too: built on what you ticked.)" : "";
        if (MessageBox.Show(this, $"Take away {list}?{also}\n\nWhat the builder wrote into this RFA-Mod folder goes; your games' folders aren't touched. You can build them again at any time.",
                            "Guerrilla Builder", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        await Work("Taking away", log => Parts.Remove(setup, parts, log));
    }

    async Task Work(string what, Func<BuildLog, bool> work)
    {
        working = true;
        BuildButton.IsEnabled = RemoveButton.IsEnabled = PartRows.IsEnabled = GuerrillaBox.IsEnabled = GameBox.IsEnabled = false;
        LogBox.Clear();
        Progress.Visibility = Visibility.Visible;
        StatusText.Text = what + "...";
        bool ok;
        using (var log = new BuildLog(setup.LogFile, line => Dispatcher.InvokeAsync(() => Append(line))))
        {
            log.Stage = stage => Dispatcher.InvokeAsync(() => StatusText.Text = stage);
            ok = await Task.Run(() =>
            {
                try { return work(log); }
                catch (Exception error)
                {
                    log.Line($"Stopped by an error: {error.Message}");
                    log.Detail(error.ToString());
                    return false;
                }
            });
        }
        working = false;
        BuildButton.IsEnabled = RemoveButton.IsEnabled = PartRows.IsEnabled = GuerrillaBox.IsEnabled = GameBox.IsEnabled = true;
        Progress.Visibility = Visibility.Hidden;
        ShowParts(tick: true);
        StatusText.Text = !ok ? $"Stopped: the last lines above say why (all of it is in {setup.LogFile})."
            : what == "Building" ? "Done. Start the game with RFAModLauncher.exe; in the developer console (the ` key) type: level rfg_12_11"
            : "Done.";
    }

    void Append(string line)
    {
        LogBox.AppendText(line + Environment.NewLine);
        LogBox.ScrollToEnd();
    }

    void OnOpenLog(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(setup.LogFile))
        {
            StatusText.Text = "Nothing has been built or taken away yet, so there's no log.";
            return;
        }
        Process.Start(new ProcessStartInfo(setup.LogFile) { UseShellExecute = true });
    }

    void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!working) return;
        var answer = MessageBox.Show(this, "Stop now? What is being built is left unfinished: build it again later.", "Guerrilla Builder",
                                     MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer == MessageBoxResult.Yes) Environment.Exit(1);
        e.Cancel = true;
    }

    Brush Resource(string key) => (Brush)FindResource(key);

    static Brush Frozen(Color colour)
    {
        var brush = new SolidColorBrush(colour);
        brush.Freeze();
        return brush;
    }
}
