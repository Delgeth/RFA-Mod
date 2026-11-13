using System.IO;
using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using RfaFormats.Meshes;
using RfaFormats.Zones;

namespace RfaEditor;

// Terrain pieces: new terrain made as level geometry (a stitch piece of its own, see TerrainPiece),
// so it is picked, moved, turned, duplicated and deleted like any piece, synced live, and saved into
// the stitch list with its files brought into the level's containers. Its look is another stitch
// piece's (its material and textures): any of the game's, the Guerrilla world's among them.
//
// The toolbar's terrain piece button makes one where the view points; with a terrain piece picked it
// edits that one (a new piece in its place, its shape changed: undo puts the old one back). The
// shapes are kept in the mod's folder (editor_terrain_pieces.txt), so they can be edited later.
public partial class LevelEditor
{
    sealed record PieceRecipe(string Template, TerrainPieceShape Shape);

    static Dictionary<string, PieceRecipe>? recipes;
    string RecipesFile => Path.Combine(Workspace.ModFolder, "editor_terrain_pieces.txt");
    TerrainPieceShape lastShape = new("Hill", 48, 12, 24, 1, 48, 4, 6);
    string? lastTemplate;

    Dictionary<string, PieceRecipe> Recipes()
    {
        if (recipes != null) return recipes;
        recipes = new(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(RecipesFile))
                foreach (string line in File.ReadAllLines(RecipesFile))
                    if (line.Split('|') is [var name, var template, var text] && TerrainPieceShape.Parse(text) is { } shape)
                        recipes[name] = new PieceRecipe(template, shape);
        }
        catch (IOException) { }
        return recipes;
    }

    void RememberRecipe(string piece, PieceRecipe recipe)
    {
        Recipes()[piece] = recipe;
        try
        {
            Directory.CreateDirectory(Workspace.ModFolder);
            File.WriteAllLines(RecipesFile, Recipes().Select(r => $"{r.Key}|{r.Value.Template}|{r.Value.Shape.Text}"));
        }
        catch (IOException ex) { context.Log($"Terrain piece: its shape couldn't be kept in {RecipesFile} ({ex.Message})."); }
    }

    void OnTerrainPieceClicked(object sender, RoutedEventArgs e)
    {
        if (!context.Assets.IndexReady)
        {
            context.Log("The editor is still reading the game's files. Try again in a few seconds.");
            return;
        }
        if (stitchListName == null)
        {
            MessageBox.Show($"{zoneName} has no stitch list to add level geometry to.", "Level editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        ZoneObject? editing = selected is { } picked && IsGeometry(picked) && Recipes().ContainsKey(PieceOf(picked)) ? picked : null;
        PieceRecipe? start = editing != null ? Recipes()[PieceOf(editing)] : null;
        if (AskPiece(start, editing != null) is not { } recipe) return;
        string? piece = MakePiece(recipe);
        if (piece == null) return;
        if (editing != null) SwapPiece(editing, piece);
        else AddGeometry(piece, PointAhead());
    }

    /// <summary>Makes a terrain piece's files (kept by the editor until saved); returns its name, or null (said why).</summary>
    string? MakePiece(PieceRecipe recipe)
    {
        string template = Path.GetFileNameWithoutExtension(recipe.Template);
        byte[]? c = context.Assets.ReadAnywhere(template + ".cstch_pc"), g = context.Assets.ReadAnywhere(ModelFile.DataFileName(template + ".cstch_pc"));
        if (c == null || g == null)
        {
            MessageBox.Show($"{template}'s files aren't in the game's files.", "Terrain piece", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }
        if (TerrainPiece.Unsuitable(c, g) is string why)
        {
            MessageBox.Show($"{template} can't give a terrain piece its look: {why}. Pick another.", "Terrain piece", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }
        // Named by what it is made of, so the same shape is the same piece.
        ulong hash = 14695981039346656037;
        foreach (char ch in template + "|" + recipe.Shape.Text) hash = unchecked((hash ^ char.ToLowerInvariant(ch)) * 1099511628211);
        string name = $"tp_{hash & 0xFFFFFFFFFF:x10}";
        try
        {
            (byte[] madeC, byte[] madeG) = TerrainPiece.Write(template, c, g, name, recipe.Shape);
            ModelFile.Read(name + ".cstch_pc", madeC, madeG);  // reads back
            context.Assets.AddGenerated(name + ".cstch_pc", madeC, like: template + ".cstch_pc");
            context.Assets.AddGenerated(ModelFile.DataFileName(name + ".cstch_pc"), madeG);
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or ArgumentException or IndexOutOfRangeException)
        {
            MessageBox.Show($"The terrain piece couldn't be made over {template}: {ex.Message}.", "Terrain piece", MessageBoxButton.OK, MessageBoxImage.Warning);
            return null;
        }
        RememberRecipe(name, recipe);
        lastShape = recipe.Shape;
        lastTemplate = template;
        context.Log($"Terrain piece {name}: {recipe.Shape.Kind.ToLowerInvariant()}, {recipe.Shape.Size:0} m across, {recipe.Shape.Height:0} m high, looking like {template}.");
        return name;
    }

    /// <summary>A geometry object's piece swapped for another, where it is (as one change: undo puts the old one back).</summary>
    void SwapPiece(ZoneObject old, string piece)
    {
        CommitEdit();
        (Vector3 position, Matrix4x4 rotation) = old.Placement;
        int list = IntOf(old, "stitch_list", 0), flags = IntOf(old, "stitch_flags", 4);
        var taken = zone.Objects.Select(o => o.Handle).ToHashSet();
        uint handle = GeometryHandles;
        while (taken.Contains(handle)) handle++;
        ZoneObject made = NewGeometryObject(piece, list, flags, position, rotation, handle);
        NoteStitchToAdd(piece);
        RemovedObjects removedOld = zone.Remove(old);
        scene?.Remove(old);
        scene?.Add(made);
        RemovedObjects? removedNew = null;
        Record(new Change(made, "Edit terrain piece",
            Undo: () => { removedNew = zone.Remove(made); scene?.Remove(made); zone.PutBack(removedOld); scene?.Add(old); },
            Redo: () => { removedOld = zone.Remove(old); scene?.Remove(old); if (removedNew != null) zone.PutBack(removedNew); scene?.Add(made); },
            AddsOrRemoves: true));
        ObjectsChanged();
        PickInOutliner(made);
    }

    /// <summary>A piece's files to bring into the stitch list's containers on saving, if they haven't it (as AddGeometry does).</summary>
    void NoteStitchToAdd(string piece)
    {
        if (stitchesToAdd.ContainsKey(piece) || !StitchContainers().Any(c => Workspace.Container(c.Archive, c.Container).Find(piece + ".cstch_pc") == null)) return;
        if (context.Assets.StitchPackageOf(piece, out string? why) is { } package) stitchesToAdd[piece] = package;
        else context.Log($"Terrain piece {piece}: its files can't be brought in: {why}.");
    }

    /// <summary>The look choices: this zone's pieces that can give one first, then every stitch piece in the game (checked when chosen).</summary>
    List<string> PieceLooks()
    {
        var here = zone.Objects.Where(IsGeometry).Select(PieceOf).Where(p => !p.StartsWith("tp_", StringComparison.OrdinalIgnoreCase))
                       .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        var suitable = new List<string>();
        foreach (string piece in here)
        {
            byte[]? c = context.Assets.ReadAnywhere(piece + ".cstch_pc"), g = context.Assets.ReadAnywhere(ModelFile.DataFileName(piece + ".cstch_pc"));
            if (c != null && g != null && TerrainPiece.Unsuitable(c, g) == null) suitable.Add(piece);
        }
        var everything = context.Assets.NamesWhere(n => n.EndsWith(".cstch_pc", StringComparison.OrdinalIgnoreCase))
                                .Select(n => n[..^".cstch_pc".Length]).Where(n => !n.StartsWith("tp_", StringComparison.OrdinalIgnoreCase))
                                .Except(suitable, StringComparer.OrdinalIgnoreCase).Distinct(StringComparer.OrdinalIgnoreCase)
                                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
        return [.. suitable, .. everything];
    }

    /// <summary>The terrain piece dialog; null if cancelled.</summary>
    PieceRecipe? AskPiece(PieceRecipe? start, bool editing)
    {
        TerrainPieceShape s = start?.Shape ?? lastShape;
        List<string> looks = PieceLooks();
        if (looks.Count == 0)
        {
            MessageBox.Show("No stitch pieces were found in the game's files to give a terrain piece its look.", "Terrain piece", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }
        var window = new Window
        {
            Title = editing ? "Edit terrain piece" : "New terrain piece",
            Owner = Window.GetWindow(this),
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        if (TryFindResource("WindowBrush") is System.Windows.Media.Brush back) window.Background = back;
        if (TryFindResource("TextBrush") is System.Windows.Media.Brush fore) window.Foreground = fore;
        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        int row = 0;
        void Row(string label, FrameworkElement control, string? tip = null)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBlock { Text = label, Margin = new Thickness(0, 4, 10, 4), VerticalAlignment = VerticalAlignment.Center, ToolTip = tip };
            control.Margin = new Thickness(0, 3, 0, 3);
            control.ToolTip ??= tip;
            Grid.SetRow(text, row);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            grid.Children.Add(text);
            grid.Children.Add(control);
            row++;
        }
        static string N(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
        var kind = new ComboBox { ItemsSource = TerrainPieceShape.Kinds, SelectedItem = TerrainPieceShape.Kinds.Contains(s.Kind) ? s.Kind : "Hill" };
        var size = new TextBox { Text = N(s.Size) };
        var height = new TextBox { Text = N(s.Height) };
        var feature = new TextBox { Text = N(s.Feature) };
        var seed = new TextBox { Text = s.Seed.ToString() };
        var detail = new TextBox { Text = s.Resolution.ToString() };
        var skirt = new TextBox { Text = N(s.Skirt) };
        var tile = new TextBox { Text = N(s.Tile) };
        string startLook = start?.Template ?? lastTemplate ?? looks[0];
        var look = new ComboBox { ItemsSource = looks, IsEditable = true, IsTextSearchEnabled = true, Text = startLook, MaxDropDownHeight = 400 };
        Row("Landform", kind);
        Row("Size (m)", size, "How far it reaches across, both ways");
        Row("Height (m)", height, "How high it rises (a crater's bowl goes down by most of it)");
        Row("Feature size (m)", feature, "How big its bumps, crags and dunes are");
        Row("Seed", seed, "The same seed makes the same piece again");
        Row("Detail", detail, "Squares across (8 to 120): more is smoother and heavier");
        Row("Sides (m)", skirt, "How far its sides reach down below its lowest point, so it shows no gap on uneven ground (0: none)");
        Row("Texture repeat (m)", tile, "How many metres the texture covers before it repeats");
        Row("Look like", look, "Which stitch piece's material and textures it has: this zone's (that can give one) first, then every piece in the game and the Guerrilla world");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = editing ? "Change" : "Make", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        ok.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(buttons, row);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);
        window.Content = grid;
        if (window.ShowDialog() != true) return null;
        static float Num(TextBox box, float fallback, float least, float most) =>
            float.TryParse(box.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) && float.IsFinite(v)
                ? Math.Clamp(v, least, most) : fallback;
        var shape = new TerrainPieceShape(kind.SelectedItem as string ?? "Hill", Num(size, s.Size, 2, 500), Num(height, s.Height, -200, 400),
                                          Num(feature, s.Feature, 1, 500), int.TryParse(seed.Text, out int sd) ? sd : s.Seed,
                                          (int)Num(detail, s.Resolution, 8, 120), Num(skirt, s.Skirt, 0, 100), Num(tile, s.Tile, 0.5f, 100));
        string chosen = (look.Text ?? "").Trim();
        if (chosen.Length == 0) chosen = startLook;
        return new PieceRecipe(Path.GetFileNameWithoutExtension(chosen), shape);
    }
}
