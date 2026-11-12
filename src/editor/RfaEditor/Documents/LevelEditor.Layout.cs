using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace RfaEditor;

// The level editor's frame (LevelEditor.xaml): the parts of it the window shows elsewhere (its menus
// in the title bar, its status in the status line; its Scene panel is the outliner), what the menus
// do beyond the buttons they mirror, how fast the view is drawn, and what the window asks of a zone it
// opens.
public partial class LevelEditor : IDocumentParts
{
    FrameworkElement MenuPanel { get; set; } = null!;
    FrameworkElement StatusPanel { get; set; } = null!;

    /// <summary>
    /// Takes the parts the window shows elsewhere out of the tab's XAML (where they are laid out with
    /// the rest, so they can be designed and wired up together). The menus keep the tab's names, so
    /// their ties to its buttons (View > Models to the Models button, ...) hold in the title bar.
    /// </summary>
    void DetachWindowParts()
    {
        StatusPanel = DocumentParts.Detach(SourceText);
        MenuPanel = DocumentParts.Detach(MenuPart);
        NameScope.SetNameScope(MenuPart, NameScope.GetNameScope(this));
        UseAssetDrops();
        Loaded += (_, _) => StartFrameStats();
        Unloaded += (_, _) => StopFrameStats();
    }

    /// <summary>The parts of this zone the window shows in other places than the tab.</summary>
    public FrameworkElement? PartFor(string panel) => panel switch
    {
        "menu" => MenuPanel,
        "status" => StatusPanel,
        "assets" => AssetsPanel(),
        _ => null,
    };

    /// <summary>
    /// Draws the zone with Direct3D 11, the view that lights it as the game does, rather than with
    /// WPF's own 3D. If the graphics card can't, the log says why and WPF's view stays (as before).
    /// </summary>
    internal void UseDirect3DView()
    {
        if (GpuViewBox.IsChecked != true) GpuViewBox.IsChecked = true;  // starts it (OnGpuViewChanged)
    }

    // ---- How fast the view is drawn --------------------------------------------------------------
    // Shown in the view's corner, brought up to date every frame:
    //   fps, frame   how often the window draws itself (WPF does, at most as often as the screen
    //                shows a new picture), from the time between frames, evened out over a few;
    //   cpu, gpu     what the last frame of the level took: the processor to hand it to the
    //                graphics card, and the card to draw it (its own clock's say);
    //   drawn        how many frames of the level the card drew in the last second. The level is
    //                drawn again only when something changed (the camera moved, an object, the
    //                light), so it is 0 while nothing does.

    TimeSpan lastRenderingTime;
    double frameMilliseconds;  // the time between frames, evened out
    readonly Queue<(DateTime When, int Drawn)> drawnLately = new();

    void StartFrameStats()
    {
        lastRenderingTime = TimeSpan.Zero;
        frameMilliseconds = 0;
        drawnLately.Clear();
        CompositionTarget.Rendering -= ShowFrameStats;
        CompositionTarget.Rendering += ShowFrameStats;
    }

    void StopFrameStats() => CompositionTarget.Rendering -= ShowFrameStats;

    void ShowFrameStats(object? sender, EventArgs e)
    {
        // WPF may call this more than once a frame: a frame is a new rendering time.
        if (e is not RenderingEventArgs { RenderingTime: var time } || time == lastRenderingTime) return;
        if (lastRenderingTime != TimeSpan.Zero)
        {
            double interval = (time - lastRenderingTime).TotalMilliseconds;
            frameMilliseconds = frameMilliseconds == 0 ? interval : frameMilliseconds * 0.9 + interval * 0.1;
        }
        lastRenderingTime = time;
        if (!IsVisible || frameMilliseconds <= 0) return;

        string text = $"fps   {1000 / frameMilliseconds,6:0}\nframe {frameMilliseconds,6:0.0} ms";
        if (GpuViewBox.IsChecked == true && gpuView != null)
        {
            gpuView.ReadTiming();
            DateTime now = DateTime.Now;
            drawnLately.Enqueue((now, gpuView.FramesDrawn));
            while (drawnLately.Count > 1 && now - drawnLately.Peek().When > TimeSpan.FromSeconds(1)) drawnLately.Dequeue();
            int drawn = gpuView.FramesDrawn - drawnLately.Peek().Drawn;
            text += $"\ncpu   {gpuView.CpuMilliseconds,6:0.0} ms\ngpu   {gpuView.GpuMilliseconds,6:0.0} ms\ndrawn {drawn,6} /s";
        }
        else text += "\ndrawn by WPF";
        FrameStats.Text = text;
    }

    // ---- The menus ---------------------------------------------------------------------------------

    /// <summary>Tools > Select, Move, Rotate or Sculpt (named in the item's Tag): as their buttons and keys.</summary>
    void OnToolMenu(object sender, RoutedEventArgs e)
    {
        switch ((sender as FrameworkElement)?.Tag as string)
        {
            case "Select": SetTool(LevelTool.Select); break;
            case "Rotate": SetTool(LevelTool.Rotate); break;
            case "Sculpt": ChooseSculpt(); break;
            default: SetTool(LevelTool.Move); break;
        }
        ViewArea.Focus();
    }

    /// <summary>Tools > Snap step: the toolbar's steps, the chosen one ticked.</summary>
    void OnSnapStepMenuOpened(object sender, RoutedEventArgs e)
    {
        SnapStepMenu.Items.Clear();
        for (int i = 0; i < GridSizeBox.Items.Count; i++)
        {
            int index = i;
            var item = new MenuItem { Header = $"{(GridSizeBox.Items[i] as ComboBoxItem)?.Content} m", IsChecked = GridSizeBox.SelectedIndex == i };
            item.Click += (_, _) => GridSizeBox.SelectedIndex = index;
            SnapStepMenu.Items.Add(item);
        }
    }

    /// <summary>Help > Mouse and keys: every mouse button and key of the 3D view.</summary>
    async void OnHelpKeys(object sender, RoutedEventArgs e)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, LineHeight = 21 };
        void Line(string bold, string rest)
        {
            text.Inlines.Add(new Run(bold) { FontWeight = FontWeights.SemiBold });
            text.Inlines.Add(new Run("  " + rest));
            text.Inlines.Add(new LineBreak());
        }
        Line("Left click", "pick an object. Drag an arrow or ring: move or turn it.");
        Line("Right drag", "look around. Hold it and use W A S D to fly, Q / E down and up, Shift faster.");
        Line("Middle drag", "slide the view. Wheel: move forwards and back.");
        Line("Alt + left drag", "circle around the picked object.");
        Line("Q / W / E / T", "the select, move, rotate and sculpt tools. M: magnets on or off.");
        Line("F", "look at the picked object. Esc: pick nothing.");
        Line("Ctrl+D", "duplicate the picked object. Del: delete it. Edit > Add adds an object.");
        Line("Ctrl+Z / Ctrl+Y", "undo / redo. Ctrl+S: save.");
        Line("Sculpting", "hold the left button; Shift does the opposite; [ and ] change the brush's size.");
        text.Inlines.Add(new Run("With an Xbox controller, the log lists its buttons when it connects; the editor guide has them all."));
        try
        {
            await new Wpf.Ui.Controls.MessageBox { Title = "Mouse and keys in the 3D view", Content = text, CloseButtonText = "Close", MaxWidth = 620 }
                .ShowDialogAsync();
        }
        catch (Exception ex)
        {
            context.Log("The help couldn't be shown: " + ex.Message);
        }
    }
}
