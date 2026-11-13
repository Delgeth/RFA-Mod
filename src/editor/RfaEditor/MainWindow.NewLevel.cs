using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using RfaFormats;
using RfaFormats.Levels;

namespace RfaEditor;

// "New level": a brand-new level for one of the game's modes, listed in that mode's map list (see
// NewLevel). "Spawn resources": what a level can spawn (its spawn_resource.xtbl entry), changed.
public partial class MainWindow
{
    const string TablesArchive = @"build\pc\cache\table.vpp_pc";

    Window Dialog(string title)
    {
        var window = new Window
        {
            Title = title,
            Owner = this,
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        if (TryFindResource("WindowBrush") is System.Windows.Media.Brush back) window.Background = back;
        if (TryFindResource("TextBrush") is System.Windows.Media.Brush fore) window.Foreground = fore;
        return window;
    }

    /// <summary>A label-and-control grid for a dialog; Row adds a line.</summary>
    sealed class DialogForm
    {
        public readonly Grid Grid = new() { Margin = new Thickness(12) };
        int row;
        public DialogForm(double width)
        {
            Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        }
        public void Row(string label, FrameworkElement control, string? tip = null)
        {
            Grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBlock { Text = label, Margin = new Thickness(0, 4, 10, 4), VerticalAlignment = VerticalAlignment.Top, ToolTip = tip };
            control.Margin = new Thickness(0, 3, 0, 3);
            control.ToolTip ??= tip;
            Grid.SetRow(text, row);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            Grid.Children.Add(text);
            Grid.Children.Add(control);
            row++;
        }
        public void Buttons(Window window, string okText)
        {
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var ok = new Button { Content = okText, IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
            var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
            ok.Click += (_, _) => window.DialogResult = true;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            Grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(buttons, row);
            Grid.SetColumnSpan(buttons, 2);
            Grid.Children.Add(buttons);
            window.Content = Grid;
        }
    }

    // ---- New level ---------------------------------------------------------------------------------

    void OnNewLevel(object sender, RoutedEventArgs e)
    {
        if (!context.Assets.IndexReady)
        {
            Log("New level: the editor is still reading the game's files. Try again in a few seconds.");
            return;
        }
        Window window = Dialog("New level");
        var form = new DialogForm(300);
        var mode = new ComboBox { ItemsSource = new[] { "Ruin", "Infestation" }, SelectedIndex = 0 };
        var template = new ComboBox();
        void FillTemplates()
        {
            var levels = NewLevel.LevelsOf(workspace, mode.SelectedIndex == 0 ? NewLevel.Mode.Ruin : NewLevel.Mode.Infestation)
                                 .Where(l => File.Exists(Path.Combine(workspace.GameFolder, @"build\pc\cache", l + ".vpp_pc"))).ToList();
            template.ItemsSource = levels;
            template.SelectedIndex = levels.Count > 0 ? 0 : -1;
        }
        mode.SelectionChanged += (_, _) => FillTemplates();
        FillTemplates();
        var fileName = new TextBox { Text = "" };
        var displayName = new TextBox { Text = "" };
        var blank = new CheckBox { Content = "Blank: only its starts, spawns and mode objects", IsChecked = true };
        var ground = new ComboBox { ItemsSource = new[] { "New, flat (made from nothing: to sculpt and paint)", "None (for caves and interiors built from pieces)" }, SelectedIndex = 0 };
        var sizes = new[] { 32, 64, 96, 128, 160, 192, 224, 255 };
        var groundSize = new ComboBox { ItemsSource = sizes.Select(v => v == 255 ? "255 m (the whole zone)" : $"{v} m").ToList(), SelectedIndex = sizes.Length - 1 };
        var skies = NewLevel.Skies(workspace);
        var sky = new ComboBox { ItemsSource = skies.Prepend("(the start level's)").ToList(), SelectedIndex = 0, IsEditable = true, MaxDropDownHeight = 400 };
        void GroundChoices() => groundSize.IsEnabled = ground.IsEnabled && ground.SelectedIndex == 0;
        ground.SelectionChanged += (_, _) => GroundChoices();
        blank.Checked += (_, _) => { ground.IsEnabled = true; sky.IsEnabled = true; GroundChoices(); };
        blank.Unchecked += (_, _) => { ground.IsEnabled = false; sky.IsEnabled = false; GroundChoices(); };
        mode.SelectionChanged += (_, _) => ground.SelectedIndex = mode.SelectedIndex == 0 ? 0 : 1;  // (Infestation's levels have none to flatten)
        var picture = new TextBox { IsReadOnly = true, Text = "" };
        var browse = new Button { Content = "Choose...", Margin = new Thickness(6, 0, 0, 0), MinWidth = 70 };
        var clear = new Button { Content = "None", Margin = new Thickness(6, 0, 0, 0), MinWidth = 50 };
        browse.Click += (_, _) =>
        {
            var pick = new OpenFileDialog { Title = "A picture for the level's preview", Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|All files|*.*" };
            if (pick.ShowDialog(window) == true) picture.Text = pick.FileName;
        };
        clear.Click += (_, _) => picture.Text = "";
        var pictureRow = new DockPanel();
        DockPanel.SetDock(clear, Dock.Right);
        DockPanel.SetDock(browse, Dock.Right);
        pictureRow.Children.Add(clear);
        pictureRow.Children.Add(browse);
        pictureRow.Children.Add(picture);
        form.Row("Mode", mode, "Which mode's map list it is in");
        form.Row("Start from", template, "The level of that mode it's made from (its ground, its starts and spawns, its mode objects, its spawn resources)");
        form.Row("File name", fileName, "The level's file name (its archive, <name>.vpp_pc): lower case letters, digits and _");
        form.Row("Name in the menu", displayName, "What the map list shows (every language)");
        form.Row("", blank, "Off: a full copy of the level, its ground, buildings and scenery too");
        form.Row("Ground", ground, "New: ground made from nothing (flat, at the level's height). None: no ground at all (as Infestation's levels in their caves): " +
                                   "build the floor, walls and ceiling from stitch pieces, terrain pieces and chunks");
        form.Row("Ground size", groundSize, "How big the new ground is: a square round the level's starts and spawns (the level's middle); past it the level ends in a drop");
        form.Row("Sky", sky, "The level's sky (the game's sky effects): its environment boxes name it, and its files go with the level");
        form.Row("Preview picture", pictureRow, "Shown for it in the map list (and Infestation's loading screen): fitted to the game's picture size, its middle kept. None: the start level's picture");
        form.Buttons(window, "Make");
        if (window.ShowDialog() != true) return;

        string name = fileName.Text.Trim().ToLowerInvariant();
        string shown = displayName.Text.Trim();
        if (template.SelectedItem is not string start) { Log("New level: no level to start from."); return; }
        if (!Regex.IsMatch(name, "^[a-z][a-z0-9_]{1,30}$")) { Log("New level: the file name has to be lower case letters, digits and _ (2 to 31, a letter first)."); return; }
        if (shown.Length == 0) shown = name;
        var chosenMode = mode.SelectedIndex == 0 ? NewLevel.Mode.Ruin : NewLevel.Mode.Infestation;
        bool replace = false;
        if (File.Exists(Path.Combine(workspace.OverridesFolder, $@"build\pc\cache\{name}.vpp_pc")))
        {
            if (MessageBox.Show(this, $"There is already a level {name} made by the editor. Make it again, from {start}? Its edits are lost.",
                                "New level", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            foreach (var document in documents.Where(d => d.Key.StartsWith($@"level:build\pc\cache\{name}.vpp_pc", StringComparison.OrdinalIgnoreCase)).ToList())
                document.Value.Tab.Close();
            replace = true;
        }
        Mouse.OverrideCursor = Cursors.Wait;
        try
        {
            List<string> report = new NewLevel(workspace, context.Assets).Make(chosenMode, start, name, shown, blank.IsChecked == true, picture.Text.Length > 0 ? picture.Text : null, replace, ground.SelectedIndex == 1,
                sizes[Math.Max(0, groundSize.SelectedIndex)], sky.SelectedIndex == 0 || string.IsNullOrWhiteSpace(sky.Text) || sky.Text.StartsWith('(') ? null : sky.Text.Trim());
            Log($"New level {name} ({chosenMode}, from {start}):");
            foreach (string line in report) Log("  " + line);
            Log("  The game reads its map lists at start-up: restart it to see the level in the menu.");
            RefreshOverrideMarks();
            BuildTree();
            _ = BuildSearchIndexAsync();
            string archive = $@"build\pc\cache\{name}.vpp_pc";
            Packfile pack = workspace.Archive(archive);
            if (pack.Find(name + ".rfgzone_pc") is { } zone) OpenLevel(new GameFile(pack, zone, archive, null));
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or InvalidOperationException
                                       or System.Runtime.InteropServices.COMException or FormatException)
        {
            Log($"New level: {name} couldn't be made: {ex.Message}");
            EditorLog.Write($"  where: {ex}");
        }
        finally
        {
            Mouse.OverrideCursor = null;
        }
    }

    // ---- Delete level ------------------------------------------------------------------------------

    /// <summary>Deletes a level the editor made, all of it (see <see cref="NewLevel.Remove"/>).</summary>
    void OnDeleteLevel(object sender, RoutedEventArgs e)
    {
        List<NewLevel.MadeLevel> made = NewLevel.Made(workspace);
        if (made.Count == 0)
        {
            Log("Delete level: there are no levels made with New level.");
            return;
        }
        Window window = Dialog("Delete level");
        var form = new DialogForm(360);
        var list = new ListBox { ItemsSource = made, SelectionMode = SelectionMode.Extended, Height = 180, SelectedIndex = 0 };
        form.Row("Levels made", list, "The levels made with New level (and any left in no map list, or only their edits). Ctrl or Shift: more than one");
        form.Row("", new TextBlock
        {
            Text = "Deletes each from its map list, its spawn resources, its name in every language, its pictures, and its archive with all its edits. It can't be undone.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Width = 360,
        });
        form.Buttons(window, "Delete");
        if (window.ShowDialog() != true) return;
        var chosen = list.SelectedItems.Cast<NewLevel.MadeLevel>().ToList();
        if (chosen.Count == 0) return;
        if (MessageBox.Show(this, $"Delete {string.Join(", ", chosen.Select(c => c.Name))} for good? This can't be undone.", "Delete level",
                            MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        foreach (NewLevel.MadeLevel level in chosen)
        {
            foreach (var document in documents.Where(d => d.Key.StartsWith($@"level:build\pc\cache\{level.Name}.vpp_pc", StringComparison.OrdinalIgnoreCase)).ToList())
                document.Value.Tab.Close();
            try
            {
                Log($"Deleted level {level.Name}:");
                foreach (string line in new NewLevel(workspace, context.Assets).Remove(level.Name)) Log("  " + line);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
            {
                Log($"Delete level: {level.Name} couldn't be deleted: {ex.Message}");
            }
        }
        Log("  The game reads its map lists at start-up: restart it.");
        RefreshOverrideMarks();
        BuildTree();
        _ = BuildSearchIndexAsync();
    }

    // ---- Spawn resources ---------------------------------------------------------------------------

    /// <summary>A level with a row naming its spawn resources: which table, its key, the entry it names.</summary>
    sealed record ResourceUser(string Table, string Row, string KeyTag, string Key, string Kind, string Entry)
    {
        public override string ToString() => $"{Key}  ({Kind}: {Entry})";
    }

    List<ResourceUser> ResourceUsers()
    {
        var users = new List<ResourceUser>();
        foreach (var (table, row, key, kind) in new[]
                 {
                     ("wrecking_crew_maps.xtbl", "wc_map_list", "file_name", "Ruin"),
                     ("mp_levels.xtbl", "mp_level_list", "Filename", "Multiplayer"),
                     ("missions.xtbl", "mission", "Name", "Mission"),
                 })
        {
            string text;
            try { text = Encoding.Latin1.GetString(workspace.Read(TablesArchive, table)); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { continue; }
            foreach (Match m in Regex.Matches(text, $@"<{row}>.*?</{row}>", RegexOptions.Singleline))
            {
                string k = Regex.Match(m.Value, $@"<{key}>([^<]*)</{key}>").Groups[1].Value.Trim();
                string entry = Regex.Match(m.Value, @"<resource_reservation>([^<]*)</resource_reservation>").Groups[1].Value.Trim();
                if (k.Length > 0 && entry.Length > 0) users.Add(new ResourceUser(table, row, key, k, kind, entry));
            }
        }
        return users;
    }

    string? lastResourceLevel;

    void OnSpawnResources(object sender, RoutedEventArgs e)
    {
        string resources;
        List<string> characters, vehicles, exos;
        try
        {
            resources = Encoding.Latin1.GetString(workspace.Read(TablesArchive, "spawn_resource.xtbl"));
            characters = SpawnResource.RowNames(Encoding.Latin1.GetString(workspace.Read(TablesArchive, "character.xtbl")), "Character");
            vehicles = SpawnResource.RowNames(Encoding.Latin1.GetString(workspace.Read(TablesArchive, "vehicles.xtbl")), "Vehicle");
            exos = SpawnResource.RowNames(Encoding.Latin1.GetString(workspace.Read(TablesArchive, "exo.xtbl")), "Exo");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            Log("Spawn resources: the game's tables couldn't be read: " + ex.Message);
            return;
        }
        List<ResourceUser> users = ResourceUsers();
        if (users.Count == 0) { Log("Spawn resources: no level names its spawn resources."); return; }

        Window window = Dialog("Spawn resources");
        var form = new DialogForm(380);
        var level = new ComboBox { ItemsSource = users, MaxDropDownHeight = 400 };
        var shared = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = 0.75 };
        var own = new CheckBox { Content = "Give this level its own (changing them changes no other level's)" };
        var population = new StackPanel { Orientation = Orientation.Horizontal };
        var sides = new Dictionary<string, TextBox>();
        foreach (string side in SpawnResource.Sides)
        {
            population.Children.Add(new TextBlock { Text = side, Margin = new Thickness(0, 3, 4, 0) });
            var box = new TextBox { Width = 36, Margin = new Thickness(0, 0, 10, 0) };
            sides[side] = box;
            population.Children.Add(box);
        }
        var rotate = new CheckBox { Content = "Rotate slots" };
        var look = new ComboBox { ItemsSource = characters, IsEditable = true, IsTextSearchEnabled = true, MaxDropDownHeight = 400 };

        // A list of names, with a choice to add and a button to take the picked ones out.
        (FrameworkElement View, List<string> Items, Action<IEnumerable<string>> Set) NameList(List<string> choices, string what)
        {
            var items = new List<string>();
            var box = new ListBox { Height = 110, SelectionMode = SelectionMode.Extended };
            var pick = new ComboBox { ItemsSource = choices, IsEditable = true, IsTextSearchEnabled = true, MaxDropDownHeight = 400 };
            var add = new Button { Content = "Add", MinWidth = 60, Margin = new Thickness(6, 0, 0, 0) };
            var remove = new Button { Content = "Remove", MinWidth = 60, Margin = new Thickness(6, 0, 0, 0) };
            void Show() { box.ItemsSource = null; box.ItemsSource = items; }
            add.Click += (_, _) =>
            {
                string name = (pick.Text ?? "").Trim();
                if (name.Length == 0 || items.Contains(name, StringComparer.OrdinalIgnoreCase)) return;
                if (!choices.Contains(name, StringComparer.OrdinalIgnoreCase)) Log($"Spawn resources: {name} isn't one of the game's {what} (added anyway).");
                items.Add(name);
                Show();
            };
            remove.Click += (_, _) =>
            {
                foreach (string s in box.SelectedItems.Cast<string>().ToList()) items.Remove(s);
                Show();
            };
            var line = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
            DockPanel.SetDock(remove, Dock.Right);
            DockPanel.SetDock(add, Dock.Right);
            line.Children.Add(remove);
            line.Children.Add(add);
            line.Children.Add(pick);
            var panel = new StackPanel();
            panel.Children.Add(box);
            panel.Children.Add(line);
            return (panel, items, names => { items.Clear(); items.AddRange(names); Show(); });
        }
        var (npcView, npcItems, setNpcs) = NameList(characters, "characters (character.xtbl)");
        var (vehicleView, vehicleItems, setVehicles) = NameList(vehicles, "vehicles (vehicles.xtbl)");
        var (exoView, exoItems, setExos) = NameList(exos, "exos (exo.xtbl)");

        SpawnResource? showing = null;
        void ShowLevel()
        {
            if (level.SelectedItem is not ResourceUser user) return;
            showing = SpawnResource.Read(resources, user.Entry) ?? new SpawnResource { Name = user.Entry };
            int sharing = users.Count(u => u.Entry.Equals(user.Entry, StringComparison.OrdinalIgnoreCase));
            shared.Text = sharing > 1
                ? $"{user.Entry} is used by {sharing} levels: {string.Join(", ", users.Where(u => u.Entry.Equals(user.Entry, StringComparison.OrdinalIgnoreCase)).Select(u => u.Key).Take(8))}{(sharing > 8 ? "..." : "")}"
                : $"{user.Entry} is this level's only.";
            own.IsChecked = sharing > 1;
            own.IsEnabled = sharing > 1;
            setNpcs(showing.Characters);
            setVehicles(showing.Vehicles);
            setExos(showing.Exos);
            look.Text = showing.PlayerLook ?? "";
            rotate.IsChecked = showing.RotateSlots == true;
            foreach (var (side, box) in sides) box.Text = showing.Populations.GetValueOrDefault(side) ?? "";
        }
        level.SelectionChanged += (_, _) => ShowLevel();
        level.SelectedItem = users.FirstOrDefault(u => u.Key.Equals(lastResourceLevel, StringComparison.OrdinalIgnoreCase)) ?? users[0];

        form.Row("Level", level, "The level (its row in the Ruin, multiplayer or mission table) and the spawn resources it names");
        form.Row("", shared);
        form.Row("", own);
        form.Row("Characters", npcView, "The people it can spawn (character.xtbl)");
        form.Row("Vehicles", vehicleView, "The vehicles it can spawn (vehicles.xtbl)");
        form.Row("Exos", exoView, "The exos it can spawn (exo.xtbl)");
        form.Row("Player look", look, "The character the player is, here (empty: the default)");
        form.Row("Populations", population, "How many of each side's people may be about (empty: not given)");
        form.Row("", rotate);
        form.Buttons(window, "Save");
        if (window.ShowDialog() != true || level.SelectedItem is not ResourceUser chosen || showing == null) return;
        lastResourceLevel = chosen.Key;

        var made = new SpawnResource { Name = chosen.Entry, PlayerLook = look.Text.Trim().Length > 0 ? look.Text.Trim() : null };
        made.Characters.AddRange(npcItems);
        made.Vehicles.AddRange(vehicleItems);
        made.Exos.AddRange(exoItems);
        made.RotateSlots = rotate.IsChecked == true ? true : showing.RotateSlots == null ? null : false;
        foreach (var (side, box) in sides)
            if (box.Text.Trim().Length > 0) made.Populations[side] = box.Text.Trim();
        try
        {
            if (own.IsChecked == true && own.IsEnabled)
            {
                var names = SpawnResource.Names(resources).ToHashSet(StringComparer.OrdinalIgnoreCase);
                string name = chosen.Key;
                for (int n = 2; names.Contains(name); n++) name = $"{chosen.Key}_{n}";
                made.Name = name;
                string table = Encoding.Latin1.GetString(workspace.Read(TablesArchive, chosen.Table));
                string changed = LevelFiles.WithRowValue(table, chosen.Row, chosen.KeyTag, chosen.Key, "resource_reservation", name)
                                 ?? throw new InvalidDataException($"{chosen.Table}'s row for {chosen.Key} couldn't be changed");
                workspace.WriteOverride(TablesArchive, "spawn_resource.xtbl", Encoding.Latin1.GetBytes(made.WriteTo(resources, chosen.Entry)));
                workspace.WriteOverride(TablesArchive, chosen.Table, Encoding.Latin1.GetBytes(changed));
                Log($"Spawn resources: {chosen.Key} has its own now, \"{name}\" (from {chosen.Entry}; {chosen.Table} names it).");
            }
            else
            {
                workspace.WriteOverride(TablesArchive, "spawn_resource.xtbl", Encoding.Latin1.GetBytes(made.WriteTo(resources)));
                Log($"Spawn resources: {chosen.Entry} saved.");
            }
            Log($"  {made.Characters.Count} characters, {made.Vehicles.Count} vehicles, {made.Exos.Count} exos{(made.PlayerLook != null ? ", the player as " + made.PlayerLook : "")}. " +
                "The game reads its tables at start-up: restart it for them to apply.");
            RefreshOverrideMarks();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Log("Spawn resources: couldn't be saved: " + ex.Message);
        }
    }
}
