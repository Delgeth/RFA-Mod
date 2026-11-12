using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RfaFormats.Meshes;
using RfaFormats.Textures;

namespace RfaEditor;

// The ground painter, the road painter and the terrain creator: two of the sculptor's modes (Paint
// ground, Paint road) and its "Create terrain…" button.
//
// The ground's look comes from four texture layers (the zone's terrain file names them) mixed by its
// blend map (<zone>_alpha00.tga): layer 1 is the base; the map's blue mixes in layer 2, its green
// layer 3 and its red layer 4, each over the ones before (see TerrainPainter.Weights). The map is
// stretched over the zone from its corner. The painter holds it at full size and moves its pixels
// under the brush toward the chosen layer's colour; the editor paints the ground again after each
// stroke. Saving encodes it as the game had it (same size, format and mip levels, so no list of
// sizes changes) and writes its texture file wherever the game has it, as an override.
//
// The road painter: drag along where the road goes. When the stroke ends the ground along it is
// graded (its height taken along the path and smoothed, the ground under the road set to it and
// blended back out to the brush's edge), and the chosen layer is painted on it. These are painted
// roads: Armageddon's own road meshes (decals in the sub-zones) aren't made.
//
// The terrain creator makes new ground over the whole zone: one of a few landforms, from noise, by
// a seed; mixed with the ground there and, if asked, faded out toward the zone's edges so it still
// meets the zones around. It can paint the ground by height and slope as well.
public partial class LevelEditor
{
    RgbaImage? blendImage;          // the zone's blend map, at full size, as painted now
    string? blendZone, blendName;   // whose, and its texture's name
    Vector2 blendCorner;            // where it starts (the zone's corner, x and z) and how far it reaches
    float blendWidth;
    ZoneTerrain? blendGround;       // the zone's layers
    byte[]? blendBefore;            // its pixels as a stroke began, for undoing it
    bool blendChanged, groundStroke, roadStroke;
    readonly List<Vector3> roadPath = [];
    float roadRadius;

    /// <summary>A layer's colour in the blend map (the channels that show it alone).</summary>
    static Vector3 LayerColour(int layer) => layer switch
    {
        1 => new Vector3(0, 0, 255),
        2 => new Vector3(0, 255, 0),
        3 => new Vector3(255, 0, 0),
        _ => Vector3.Zero,
    };

    static float SmoothStep(float from, float to, float x)
    {
        float t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Reads the zone's blend map for painting (once); false, said in the log, if it can't be.</summary>
    bool EnsureGroundPaint()
    {
        if (blendImage != null) return true;
        string zone = TerrainZone;
        try
        {
            if (context.Assets.ReadFile(zone + ".cterrain_pc", archive, container) is not { } file || ZoneTerrain.Read(TerrainAsSwapped(zone, file.Data)) is not { } ground)
            {
                context.Log($"Ground paint: {zone} has no terrain file with ground layers, so there is nothing to paint.");
                return false;
            }
            if (context.Assets.FindTexture(ground.BlendMap, archive, container) is not var (peg, texture))
            {
                context.Log($"Ground paint: {zone}'s blend map ({ground.BlendMap}) isn't in the game's files.");
                return false;
            }
            List<TerrainSubzone> subzones = ReadSubzones(zone);
            if (subzones.Count == 0)
            {
                context.Log($"Ground paint: {zone} has no ground.");
                return false;
            }
            blendImage = peg.Decode(texture);
            blendGround = ground;
            blendZone = zone;
            blendName = ground.BlendMap;
            blendCorner = LevelGeometry.ZoneCorner(subzones);
            blendWidth = subzones[0].ZoneWidth;
        }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or NotSupportedException or ArgumentException)
        {
            context.Log($"Ground paint: {zone}'s blend map couldn't be read ({ex.Message}).");
            return false;
        }
        ShowGroundLayers(blendGround.Layers.Count > 1 ? 1 : 0);
        context.Log($"Ground paint: {zone}'s blend map {blendName} ({blendImage.Width} × {blendImage.Height}) and its layers " +
                    string.Join(", ", blendGround.Layers.Select(l => System.IO.Path.GetFileNameWithoutExtension(l.Texture))) +
                    ". Hold the left button to paint the chosen layer (Shift: the base layer back). Paint road: drag along the road, let go to grade and paint it." +
                    (ShowTexturesBox.IsChecked == true ? "" : " Turn textures on to see the paint."));
        return true;
    }

    /// <summary>The layer box: each layer by its texture (the one swapped in, by the name it came from).</summary>
    void ShowGroundLayers(int select)
    {
        if (blendGround == null) return;
        GroundLayerBox.Items.Clear();
        for (int l = 0; l < blendGround.Layers.Count; l++)
        {
            string texture = layerSources.TryGetValue(l, out string? source) ? source : blendGround.Layers[l].Texture;
            GroundLayerBox.Items.Add(new ComboBoxItem
            {
                Content = $"{l + 1}: {System.IO.Path.GetFileNameWithoutExtension(texture)}",
                ToolTip = (l == 0 ? "The base layer (what shows where no other is painted)" : $"Layer {l + 1}, painted over the ones before it") +
                          ". Layer texture… gives it any ground texture of the game's or the Guerrilla world's.",
            });
        }
        GroundLayerBox.SelectedIndex = Math.Clamp(select, 0, blendGround.Layers.Count - 1);
    }

    // ---- A layer's texture swapped for any ground texture -------------------------------------
    //
    // A zone's ground has four layer slots; the painter can give a slot any ground texture that a zone
    // of the game's (or the Guerrilla world's) uses. The terrain file names each layer's texture, and
    // the new name is written over the old one in place: so that the file keeps its size (and no asset
    // list changes), the texture is copied under a name as long as the old one's ("tx" and a hash),
    // into a texture file of its own that saving brings into the level's containers.

    readonly Dictionary<int, string> layerSwaps = [];     // layer → the name written in its place (with ".tga")
    readonly Dictionary<int, string> layerSources = [];   // layer → the texture it is a copy of
    bool layerSwapsChanged;

    /// <summary>A zone's terrain file with the layers swapped (the bytes given, if it isn't the zone painted or none are).</summary>
    byte[] TerrainAsSwapped(string zone, byte[] data)
    {
        if (layerSwaps.Count == 0 || blendZone == null || !GroundKey(zone).Equals(blendZone, StringComparison.OrdinalIgnoreCase)) return data;
        try
        {
            foreach (var (layer, name) in layerSwaps) data = ZoneTerrain.WithLayerTexture(data, layer, name);
        }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or ArgumentException) { }
        return data;
    }

    async void OnLayerTextureClicked(object sender, RoutedEventArgs e)
    {
        if (!EnsureGroundPaint() || blendGround == null) return;
        int layer = ChosenGroundLayer;
        context.Log("Layer texture: finding every ground texture the game's zones use (once)...");
        List<(string Texture, int Zones)> all;
        Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
        try { all = await context.Assets.GroundTextures(); }
        finally { Mouse.OverrideCursor = null; }
        if (all.Count == 0)
        {
            context.Log("Layer texture: no zone's ground textures could be read.");
            return;
        }
        var window = new Window
        {
            Title = $"Layer {layer + 1}'s texture",
            Owner = Window.GetWindow(this),
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        if (TryFindResource("WindowBrush") is System.Windows.Media.Brush back) window.Background = back;
        if (TryFindResource("TextBrush") is System.Windows.Media.Brush fore) window.Foreground = fore;
        var panel = new StackPanel { Margin = new Thickness(12), Width = 380 };
        panel.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8),
            Text = $"Any ground texture a zone of the game's or the Guerrilla world's uses ({all.Count:N0}; how many zones use each in brackets). " +
                   "Type to find one. Layer 1 is the base; the others are painted over it.",
        });
        var items = all.Select(t => $"{System.IO.Path.GetFileNameWithoutExtension(t.Texture)}  ({t.Zones})").ToList();
        var box = new ComboBox { ItemsSource = items, IsEditable = true, IsTextSearchEnabled = true, MaxDropDownHeight = 420 };
        string now = layerSources.TryGetValue(layer, out string? was) ? was : blendGround.Layers[layer].Texture;
        int at = all.FindIndex(t => t.Texture.Equals(now, StringComparison.OrdinalIgnoreCase));
        if (at >= 0) box.SelectedIndex = at;
        panel.Children.Add(box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "Use it", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        ok.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);
        window.Content = panel;
        if (window.ShowDialog() != true) return;
        string typed = (box.Text ?? "").Split("  (")[0].Trim();
        var chosen = all.FirstOrDefault(t => System.IO.Path.GetFileNameWithoutExtension(t.Texture).Equals(typed, StringComparison.OrdinalIgnoreCase));
        if (chosen.Texture == null)
        {
            context.Log($"Layer texture: {typed} isn't one of the ground textures.");
            return;
        }
        SwapLayerTexture(layer, chosen.Texture);
    }

    /// <summary>Gives a layer a texture (see above), as one change to undo.</summary>
    void SwapLayerTexture(int layer, string texture)
    {
        if (blendGround == null || blendZone == null) return;
        // The slot's name as the game's file has it (its length is what the new name must have).
        if (context.Assets.ReadFile(blendZone + ".cterrain_pc", archive, container) is not { } file || ZoneTerrain.Read(file.Data) is not { } own) return;
        string old = own.Layers[layer].Texture;
        string name;
        if (texture.Equals(old, StringComparison.OrdinalIgnoreCase)) name = old;
        else
        {
            int length = old.Length - 4;  // without ".tga"
            ulong hash = 14695981039346656037;
            foreach (char ch in texture.ToLowerInvariant()) hash = unchecked((hash ^ ch) * 1099511628211);
            string core = "tx" + hash.ToString("x16");
            if (length < 8)
            {
                context.Log($"Layer texture: layer {layer + 1}'s name ({old}) is too short to give it another texture.");
                return;
            }
            string bare = core.Length >= length ? core[..length] : core + new string('_', length - core.Length);
            name = bare + ".tga";
            if (!context.Assets.IsGenerated(bare + ".cvbm_pc"))
            {
                if (context.Assets.FindTexture(texture, archive, container) is not var (peg, found))
                {
                    context.Log($"Layer texture: {texture} isn't in the game's files.");
                    return;
                }
                try
                {
                    (byte[] header, byte[] data) = peg.Single(found, name, found.MipLevels);
                    string? like = context.Assets.TextureFileName(old, archive, container);
                    context.Assets.AddGenerated(bare + ".cvbm_pc", header, like != null && like.EndsWith(".cvbm_pc", StringComparison.OrdinalIgnoreCase) ? like : null);
                    context.Assets.AddGenerated(bare + ".gvbm_pc", data);
                }
                catch (System.IO.InvalidDataException ex)
                {
                    context.Log($"Layer texture: {texture} couldn't be copied ({ex.Message}).");
                    return;
                }
            }
        }
        var before = (Swaps: new Dictionary<int, string>(layerSwaps), Sources: new Dictionary<int, string>(layerSources));
        if (name.Equals(old, StringComparison.OrdinalIgnoreCase)) { layerSwaps.Remove(layer); layerSources.Remove(layer); }
        else { layerSwaps[layer] = name; layerSources[layer] = texture; }
        var after = (Swaps: new Dictionary<int, string>(layerSwaps), Sources: new Dictionary<int, string>(layerSources));
        void Put((Dictionary<int, string> Swaps, Dictionary<int, string> Sources) state)
        {
            layerSwaps.Clear();
            foreach (var kv in state.Swaps) layerSwaps[kv.Key] = kv.Value;
            layerSources.Clear();
            foreach (var kv in state.Sources) layerSources[kv.Key] = kv.Value;
            layerSwapsChanged = true;
            if (blendZone != null && context.Assets.ReadFile(blendZone + ".cterrain_pc", archive, container) is { } f && ZoneTerrain.Read(TerrainAsSwapped(blendZone, f.Data)) is { } g)
                blendGround = g;
            ShowGroundLayers(layer);
            RepaintGround();
        }
        Put(after);
        LiveNeedsReload();
        Record(new Change(null, $"Layer {layer + 1}'s texture", Undo: () => Put(before), Redo: () => Put(after)));
        context.Log($"Layer texture: layer {layer + 1} is now {System.IO.Path.GetFileNameWithoutExtension(texture)}" +
                    (name.Equals(old, StringComparison.OrdinalIgnoreCase) ? " (the zone's own)." : $" (copied as {name}; saving brings it into the level)."));
    }

    /// <summary>Writes the layers' textures into the terrain file and brings their copies into the level's containers. Returns what was done, for the log.</summary>
    string SaveLayerSwaps()
    {
        if (!layerSwapsChanged || blendZone == null) return "";
        string name = blendZone + ".cterrain_pc";
        if (context.Assets.ReadFile(name, archive, container) is not { } file) return $" Layer textures: {name} couldn't be read, so they weren't saved.";
        try
        {
            // The terrain file: each slot as the game had it, then the swaps.
            byte[] data = file.Data;
            if (ZoneTerrain.Read(data) is { } now && TryOriginalTerrain(name) is { } original && ZoneTerrain.Read(original) is { } own)
                for (int l = 0; l < ZoneTerrain.LayerCount; l++)
                    if (!layerSwaps.ContainsKey(l) && !now.Layers[l].Texture.Equals(own.Layers[l].Texture, StringComparison.Ordinal) && now.Layers[l].Texture.Length == own.Layers[l].Texture.Length)
                        data = ZoneTerrain.WithLayerTexture(data, l, own.Layers[l].Texture);
            foreach (var (layer, swapped) in layerSwaps) data = ZoneTerrain.WithLayerTexture(data, layer, swapped);
            var said = new List<string> { PutTerrainFiles(new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { [name] = data }) };
            // The copies, into the level's containers that haven't them.
            var headers = layerSwaps.Values.Select(n => System.IO.Path.GetFileNameWithoutExtension(n) + ".cvbm_pc").Where(context.Assets.IsGenerated).ToList();
            if (headers.Count > 0)
            {
                if (context.Assets.FilesPackage("terrain_textures", headers, out string? why) is not { } package)
                    said.Add($"the textures couldn't be brought in ({why})");
                else
                    foreach (var to in ChunkContainers())
                    {
                        var missing = package.Files.Where(f => Workspace.Container(to.Archive, to.Container).Find(f.Name) == null).ToList();
                        if (missing.Count == 0) continue;
                        var names = missing.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                        said.Add($"{string.Join(", ", headers)} into {to.Container}: " +
                                 ContainerEdits.AddFiles(Workspace, to.Archive, to.Container, missing, package.Primitives.Where(p => names.Contains(p.Name)).ToList(), []).TrimEnd('.'));
                    }
            }
            layerSwapsChanged = false;
            return $" Layer textures: {string.Join("; ", said)}.";
        }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or ArgumentException or NotSupportedException)
        {
            return $" Layer textures couldn't be saved: {ex.Message}.";
        }
    }

    /// <summary>The game's own copy of a loose or contained terrain file (any place it has one), or null.</summary>
    byte[]? TryOriginalTerrain(string name)
    {
        foreach (var (inArchive, inContainer) in context.Assets.PlacesOf(name))
            if (TryOriginal(inArchive, inContainer, name) is { } original) return original;
        return null;
    }

    int ChosenGroundLayer => Math.Clamp(GroundLayerBox.SelectedIndex, 0, ZoneTerrain.LayerCount - 1);

    /// <summary>
    /// Moves the blend map's pixels within an area (x, z) toward a colour: <paramref name="paint"/>
    /// gives each pixel's share of the way (0 to 1) and colour by where it lies, or null to leave it.
    /// Returns whether any changed.
    /// </summary>
    bool PaintBlend(Vector2 min, Vector2 max, Func<float, float, (float Share, Vector3 Colour)?> paint)
    {
        if (blendImage is not { } image) return false;
        int w = image.Width, h = image.Height;
        int c0 = Math.Max(0, (int)MathF.Floor((min.X - blendCorner.X) / blendWidth * w)), c1 = Math.Min(w - 1, (int)MathF.Ceiling((max.X - blendCorner.X) / blendWidth * w));
        int r0 = Math.Max(0, (int)MathF.Floor((min.Y - blendCorner.Y) / blendWidth * h)), r1 = Math.Min(h - 1, (int)MathF.Ceiling((max.Y - blendCorner.Y) / blendWidth * h));
        bool any = false;
        byte[] px = image.Pixels;
        for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                float x = blendCorner.X + (c + 0.5f) * blendWidth / w, z = blendCorner.Y + (r + 0.5f) * blendWidth / h;
                if (paint(x, z) is not var (share, colour) || share <= 0) continue;
                share = MathF.Min(share, 1);
                int p = (r * w + c) * 4;
                for (int ch = 0; ch < 3; ch++)
                {
                    float now = px[p + ch], to = ch == 0 ? colour.X : ch == 1 ? colour.Y : colour.Z;
                    float next = now + (to - now) * share;
                    // Always at least a step toward it, so slow strokes still get there.
                    byte b = (byte)Math.Clamp(MathF.Round(next == now ? now : (to > now ? MathF.Max(next, now + 1) : MathF.Min(next, now - 1))), 0, 255);
                    if (b == px[p + ch]) continue;
                    px[p + ch] = b;
                    any = true;
                }
            }
        if (any) blendChanged = true;
        return any;
    }

    /// <summary>Starts a stroke of the ground or road painter; false if the zone's blend map can't be painted.</summary>
    bool BeginGroundStroke(bool road)
    {
        if (!EnsureGroundPaint() || blendImage == null) return false;
        blendBefore = (byte[])blendImage.Pixels.Clone();
        groundStroke = true;
        roadStroke = road;
        roadPath.Clear();
        roadRadius = BrushRadius;
        return true;
    }

    /// <summary>A step of a ground stroke: paints under the brush, or (the road painter) notes where the road goes.</summary>
    void GroundStrokeStep(Vector2 centre, float radius, float amount, bool inverse)
    {
        if (!groundStroke) return;
        if (roadStroke)
        {
            if (roadPath.Count == 0 || Vector2.Distance(new Vector2(roadPath[^1].X, roadPath[^1].Z), centre) >= MathF.Max(0.5f, roadRadius * 0.2f))
                roadPath.Add(new Vector3(centre.X, GroundHeight(centre.X, centre.Y), centre.Y));
            return;
        }
        Vector3 colour = LayerColour(inverse ? 0 : ChosenGroundLayer);
        float share = amount * 0.4f;
        PaintBlend(centre - new Vector2(radius), centre + new Vector2(radius), (x, z) =>
        {
            float t = Vector2.Distance(new Vector2(x, z), centre) / radius;
            return t >= 1 ? null : (share * (1 - t * t) * (1 - t * t), colour);
        });
    }

    /// <summary>The ground drawn again with the blend map as painted (if textures are shown).</summary>
    void RepaintGround()
    {
        if (ShowTexturesBox.IsChecked == true) BuildTerrain();
    }

    /// <summary>The blend map as painted for a zone, if it is the one being painted (the ground painter's look uses it).</summary>
    RgbaImage? PaintedBlend(string zone) =>
        blendImage != null && blendZone != null && GroundKey(zone).Equals(blendZone, StringComparison.OrdinalIgnoreCase) ? blendImage : null;

    /// <summary>
    /// Ends a ground or road stroke (if one was going on): grades and paints the road, draws the
    /// ground again and keeps it all as one change to undo. True if it was one.
    /// </summary>
    bool EndGroundStroke(Dictionary<TerrainSculpt, float[]> groundBefore)
    {
        if (!groundStroke) return false;
        groundStroke = false;
        if (blendImage == null || blendBefore is not { } before) return true;
        blendBefore = null;
        string what = "Paint ground";
        var graded = new List<(TerrainSculpt Zone, float[] Before, float[] After)>();
        if (roadStroke)
        {
            if (roadPath.Count == 0) return true;
            what = $"Paint road ({RoadLength():0} m)";
            GradeRoad();
            foreach (var (ground, then) in groundBefore)
            {
                float[] now = ground.Snapshot();
                if (!now.AsSpan().SequenceEqual(then)) graded.Add((ground, then, now));
            }
            roadPath.Clear();
        }
        byte[] after = (byte[])blendImage.Pixels.Clone();
        bool painted = !after.AsSpan().SequenceEqual(before);
        if (!painted && graded.Count == 0) return true;
        void Put(bool back)
        {
            foreach (var g in graded) ShowSculptedGround(g.Zone, g.Zone.Restore(back ? g.Before : g.After));
            if (blendImage != null && painted)
            {
                (back ? before : after).CopyTo(blendImage.Pixels, 0);
                blendChanged = true;
                RepaintGround();
            }
        }
        if (painted) RepaintGround();
        LiveNeedsReload();  // the game shows it once the level is loaded again
        Record(new Change(null, what, Undo: () => Put(back: true), Redo: () => Put(back: false)));
        return true;
    }

    float RoadLength()
    {
        float length = 0;
        for (int i = 1; i < roadPath.Count; i++) length += Vector3.Distance(roadPath[i - 1], roadPath[i]);
        return length;
    }

    /// <summary>
    /// The road painter's work at the end of a stroke: the road's height along its path smoothed;
    /// the ground under it (the inner 60% of the brush) set to it, blending back out to the brush's
    /// edge; the chosen layer painted on it with soft edges. In every zone it reaches.
    /// </summary>
    void GradeRoad()
    {
        var points = roadPath.ToList();
        float[] heights = points.Select(p => p.Y).ToArray();
        for (int pass = 0; pass < 4 && heights.Length > 2; pass++)
        {
            float[] next = (float[])heights.Clone();
            for (int i = 1; i < heights.Length - 1; i++) next[i] = (heights[i - 1] + 2 * heights[i] + heights[i + 1]) / 4;
            heights = next;
        }
        float half = roadRadius * 0.6f, outer = roadRadius;
        Vector2 min = new(points.Min(p => p.X) - outer, points.Min(p => p.Z) - outer), max = new(points.Max(p => p.X) + outer, points.Max(p => p.Z) + outer);

        // How far a place is from the road, and the road's height nearest it.
        (float Distance, float Height) Nearest(float x, float z)
        {
            var at = new Vector2(x, z);
            if (points.Count == 1) return (Vector2.Distance(at, new Vector2(points[0].X, points[0].Z)), heights[0]);
            float best = float.MaxValue, height = 0;
            for (int i = 1; i < points.Count; i++)
            {
                Vector2 a = new(points[i - 1].X, points[i - 1].Z), b = new(points[i].X, points[i].Z), ab = b - a;
                float t = ab.LengthSquared() > 0 ? Math.Clamp(Vector2.Dot(at - a, ab) / ab.LengthSquared(), 0, 1) : 0;
                float d = Vector2.Distance(at, a + ab * t);
                if (d < best) { best = d; height = heights[i - 1] + (heights[i] - heights[i - 1]) * t; }
            }
            return (best, height);
        }

        foreach (TerrainSculpt ground in sculptZones)
        {
            var parts = ground.Reshape((x, z, h) =>
            {
                if (x < min.X || x > max.X || z < min.Y || z > max.Y) return h;
                (float d, float road) = Nearest(x, z);
                if (d >= outer) return h;
                float share = d <= half ? 1 : SmoothStep(0, 1, 1 - (d - half) / (outer - half));
                return h + (road - h) * share;
            });
            if (parts.Count > 0) ShowSculptedGround(ground, parts);
        }
        Vector3 colour = LayerColour(ChosenGroundLayer);
        PaintBlend(min, max, (x, z) =>
        {
            float d = Nearest(x, z).Distance;
            return d >= half * 1.2f ? null : (1 - SmoothStep(half * 0.85f, half * 1.2f, d), colour);
        });
    }

    /// <summary>Writes the painted blend map into its texture file (every copy), if it changed. Returns what was done, for the log.</summary>
    string SaveGroundPaint()
    {
        if (!blendChanged || blendImage == null || blendName == null) return "";
        try
        {
            if (context.Assets.TextureFileName(blendName, archive, container) is not { } header || PegFile.DataFileName(header) is not { } dataName ||
                context.Assets.ReadFile(header, archive, container) is not { } headerFile ||
                context.Assets.ReadFile(dataName, headerFile.Archive, headerFile.Container) is not { } dataFile)
                return $" Ground paint: {blendName}'s texture file couldn't be found, so it wasn't saved.";
            PegFile peg = PegFile.Read(headerFile.Data, dataFile.Data);
            string wanted = System.IO.Path.GetFileNameWithoutExtension(blendName);
            if (peg.Textures.FirstOrDefault(t => System.IO.Path.GetFileNameWithoutExtension(t.Name).Equals(wanted, StringComparison.OrdinalIgnoreCase)) is not { } texture)
                return $" Ground paint: {blendName} isn't in {header}, so it wasn't saved.";
            if (texture.Width != blendImage.Width || texture.Height != blendImage.Height)
                return $" Ground paint: {blendName} is {texture.Width} × {texture.Height} in {header}, not as painted, so it wasn't saved.";
            byte[] encoded = TextureCodec.EncodeWithMips(texture.Format, blendImage, texture.MipLevels);
            byte[] data = peg.WithTextureData(texture, encoded);
            blendChanged = false;
            return $" Ground paint: {blendName} into {PutTerrainFiles(new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { [dataName] = data })}.";
        }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or NotSupportedException or ArgumentException)
        {
            return $" Ground paint couldn't be saved: {ex.Message}.";
        }
    }

    // ---- The terrain creator ------------------------------------------------------------------

    enum Landform { Flat, RollingHills, Mountains, Canyon, Plateau, Dunes, Crater }

    sealed record CreateOptions(Landform Kind, float Height, float Size, int Seed, float Mix, bool KeepEdges, bool Paint, int FlatLayer, int SlopeLayer, int HighLayer);

    void OnCreateTerrain(object sender, RoutedEventArgs e)
    {
        if (!EnsureSculpt() || sculpt == null) return;
        bool canPaint = EnsureGroundPaint();
        string[] layers = canPaint && blendGround != null
            ? blendGround.Layers.Select((l, i) => $"{i + 1}: {System.IO.Path.GetFileNameWithoutExtension(l.Texture)}").ToArray()
            : [];
        if (AskCreateOptions(layers) is not { } options) return;
        CreateTerrain(options);
    }

    /// <summary>The creator's dialog: what to make; null if cancelled.</summary>
    CreateOptions? AskCreateOptions(string[] layers)
    {
        var window = new Window
        {
            Title = "Create terrain",
            Owner = Window.GetWindow(this),
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        // The editor's colours (its dark theme), as the main window has them.
        if (TryFindResource("WindowBrush") is System.Windows.Media.Brush back) window.Background = back;
        if (TryFindResource("TextBrush") is System.Windows.Media.Brush fore) window.Foreground = fore;
        var grid = new Grid { Margin = new Thickness(12) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        int row = 0;
        void Row(string label, UIElement control, string? tip = null)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var text = new TextBlock { Text = label, Margin = new Thickness(0, 4, 10, 4), VerticalAlignment = VerticalAlignment.Center, ToolTip = tip };
            Grid.SetRow(text, row);
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            if (control is FrameworkElement fe) { fe.Margin = new Thickness(0, 3, 0, 3); fe.ToolTip ??= tip; }
            grid.Children.Add(text);
            grid.Children.Add(control);
            row++;
        }
        var kind = new ComboBox { ItemsSource = new[] { "Flat", "Rolling hills", "Mountains", "Canyon", "Plateau", "Dunes", "Crater" }, SelectedIndex = 1 };
        var height = new TextBox { Text = "40" };
        var size = new TextBox { Text = "120" };
        var seed = new TextBox { Text = Random.Shared.Next(1, 100000).ToString() };
        var mix = new Slider { Minimum = 0, Maximum = 100, Value = 100, IsSnapToTickEnabled = true, TickFrequency = 5 };
        var edges = new CheckBox { Content = "Fade out toward the zone's edges", IsChecked = true };
        var paint = new CheckBox { Content = "Paint the ground by height and slope", IsChecked = layers.Length > 0, IsEnabled = layers.Length > 0 };
        ComboBox Layers(int pick) => new() { ItemsSource = layers, SelectedIndex = layers.Length == 0 ? -1 : Math.Min(pick, layers.Length - 1), IsEnabled = layers.Length > 0 };
        var flat = Layers(0);
        var slope = Layers(2);
        var high = Layers(3);
        Row("Landform", kind);
        Row("Height (m)", height, "From the lowest to the highest point of the landform");
        Row("Feature size (m)", size, "How big its hills, ridges, dunes or canyon floor are");
        Row("Seed", seed, "The same seed makes the same ground again");
        Row("Mix (%)", mix, "100: the new ground; less mixes it with the ground there");
        Row("", edges, "So the zone still meets the zones around it");
        Row("", paint);
        Row("Flat ground layer", flat);
        Row("Slopes layer", slope);
        Row("High ground layer", high);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        var ok = new Button { Content = "Create", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 6, 0) };
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
        static float Number(TextBox box, float fallback, float least) =>
            float.TryParse(box.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float n) && float.IsFinite(n) ? MathF.Max(least, n) : fallback;
        return new CreateOptions((Landform)Math.Max(0, kind.SelectedIndex), Number(height, 40, 0), Number(size, 120, 4),
                                 int.TryParse(seed.Text, out int s) ? s : 1, (float)mix.Value / 100, edges.IsChecked == true,
                                 paint.IsChecked == true && layers.Length > 0, Math.Max(0, flat.SelectedIndex), Math.Max(0, slope.SelectedIndex), Math.Max(0, high.SelectedIndex));
    }

    /// <summary>Makes the zone's new ground (and paints it, if asked), as one change to undo.</summary>
    void CreateTerrain(CreateOptions o)
    {
        if (sculpt == null) return;
        TerrainSculpt ground = sculpt;
        CommitEdit();
        float[] groundBefore = ground.Snapshot();
        byte[]? paintBefore = blendImage != null ? (byte[])blendImage.Pixels.Clone() : null;
        Vector2 corner = ground.Corner;
        float width = ground.Width, H = o.Height, size = o.Size;
        Vector2 middle = corner + new Vector2(width / 2);

        // The ground's average height now: the landform is made about it.
        float sum = 0;
        int count = 0;
        for (int i = 0; i <= 16; i++)
            for (int j = 0; j <= 16; j++) { sum += ground.HeightAt(corner.X + width * i / 16, corner.Y + width * j / 16); count++; }
        float level = sum / count;

        float Fbm(float x, float z, float scale, int octaves, int salt)
        {
            float total = 0, weight = 1, all = 0;
            for (int k = 0; k < octaves; k++, scale *= 0.5f, weight *= 0.5f)
            {
                total += TerrainSculpt.Noise(x / scale, z / scale, o.Seed * 31 + salt + k) * weight;
                all += weight;
            }
            return total / all;
        }
        float Ridged(float x, float z, float scale, int octaves)
        {
            float total = 0, weight = 1, all = 0;
            for (int k = 0; k < octaves; k++, scale *= 0.5f, weight *= 0.5f)
            {
                float n = 1 - MathF.Abs(TerrainSculpt.Noise(x / scale, z / scale, o.Seed * 31 + 100 + k) * 2 - 1);
                total += n * n * weight;
                all += weight;
            }
            return total / all;
        }
        float angle = (o.Seed % 360) * MathF.PI / 180;
        float Target(float x, float z)
        {
            float r = Vector2.Distance(new Vector2(x, z), middle) / (width * 0.3f);
            switch (o.Kind)
            {
                case Landform.RollingHills:
                    return level + H * (Fbm(x, z, size, 4, 0) - 0.5f) * 1.6f;
                case Landform.Mountains:
                    return level - H * 0.25f + H * MathF.Pow(Ridged(x, z, size * 1.5f, 5), 1.3f) * 1.4f;
                case Landform.Canyon:
                {
                    // A winding cut across the zone (along x), with a flat floor and steep walls.
                    float offset = (Fbm(x, 0, size * 3, 3, 7) - 0.5f) * width * 0.6f;
                    float d = MathF.Abs(z - middle.Y - offset), floor = size * 0.25f, wall = size * 0.35f;
                    float rim = H * 0.15f * (Fbm(x, z, size * 0.5f, 3, 9) - 0.5f);
                    return level + H * (SmoothStep(floor, floor + wall, d) - 1) + rim * SmoothStep(floor, floor + wall, d);
                }
                case Landform.Plateau:
                {
                    float edge = r + (Fbm(x, z, size, 3, 11) - 0.5f) * 0.5f;
                    return level + H * (1 - SmoothStep(0.85f, 1.1f, edge)) + H * 0.05f * (Fbm(x, z, size * 0.3f, 2, 12) - 0.5f);
                }
                case Landform.Dunes:
                {
                    float u = (x * MathF.Cos(angle) + z * MathF.Sin(angle)) / size + (Fbm(x, z, size * 2, 2, 13) - 0.5f) * 3;
                    float wave = u - MathF.Floor(u);
                    float profile = wave < 0.75f ? wave / 0.75f : (1 - wave) / 0.25f;  // a long windward side and a steep lee
                    return level + H * (0.5f - 0.5f * MathF.Cos(MathF.PI * profile) - 0.5f);
                }
                case Landform.Crater:
                {
                    float bowl = r < 1 ? -H * (1 - r * r) : 0;
                    float rim = H * 0.35f * MathF.Exp(-MathF.Pow((r - 1) / 0.18f, 2));
                    return level + bowl + rim + H * 0.05f * (Fbm(x, z, size * 0.5f, 3, 14) - 0.5f);
                }
                default:
                    return level;
            }
        }
        float margin = width * 0.15f;
        float Share(float x, float z)
        {
            if (!o.KeepEdges) return o.Mix;
            float d = MathF.Min(MathF.Min(x - corner.X, corner.X + width - x), MathF.Min(z - corner.Y, corner.Y + width - z));
            return o.Mix * SmoothStep(0, margin, d);
        }
        var parts = ground.Reshape((x, z, h) => h + (Target(x, z) - h) * Share(x, z));
        if (parts.Count > 0) ShowSculptedGround(ground, parts);
        float[] groundAfter = ground.Snapshot();

        bool painted = false;
        if (o.Paint && blendImage != null)
        {
            float low = float.MaxValue, highest = float.MinValue;
            for (int i = 0; i <= 32; i++)
                for (int j = 0; j <= 32; j++)
                {
                    float h = ground.HeightAt(corner.X + width * i / 32, corner.Y + width * j / 32);
                    low = MathF.Min(low, h);
                    highest = MathF.Max(highest, h);
                }
            float span = MathF.Max(1, highest - low), step = MathF.Max(1, ground.Step);
            Vector3 flat = LayerColour(o.FlatLayer), steep = LayerColour(o.SlopeLayer), top = LayerColour(o.HighLayer);
            painted = PaintBlend(corner, corner + new Vector2(width), (x, z) =>
            {
                float share = Share(x, z);
                if (share <= 0) return null;
                float h = ground.HeightAt(x, z);
                float dx = (ground.HeightAt(x + step, z) - ground.HeightAt(x - step, z)) / (2 * step);
                float dz = (ground.HeightAt(x, z + step) - ground.HeightAt(x, z - step)) / (2 * step);
                float slope = MathF.Sqrt(dx * dx + dz * dz);  // rise over run
                Vector3 colour = Vector3.Lerp(flat, top, SmoothStep(0.65f, 0.9f, (h - low) / span));
                colour = Vector3.Lerp(colour, steep, SmoothStep(0.45f, 0.9f, slope));
                return (share, colour);
            });
        }
        byte[]? paintAfter = painted && blendImage != null ? (byte[])blendImage.Pixels.Clone() : null;
        if (parts.Count == 0 && !painted)
        {
            context.Log("Create terrain: nothing changed.");
            return;
        }
        if (painted) RepaintGround();
        void Put(bool back)
        {
            ShowSculptedGround(ground, ground.Restore(back ? groundBefore : groundAfter));
            if (blendImage != null && paintBefore != null && paintAfter != null)
            {
                (back ? paintBefore : paintAfter).CopyTo(blendImage.Pixels, 0);
                blendChanged = true;
                RepaintGround();
            }
        }
        LiveNeedsReload();
        string name = o.Kind switch { Landform.RollingHills => "rolling hills", _ => o.Kind.ToString().ToLowerInvariant() };
        Record(new Change(null, $"Create terrain ({name})", Undo: () => Put(back: true), Redo: () => Put(back: false)));
        context.Log($"Create terrain: {name} over {ground.Zone} ({H:0} m high, features {size:0} m, seed {o.Seed}" +
                    (o.Mix < 1 ? $", mixed {o.Mix:P0}" : "") + (o.KeepEdges ? ", faded out to the edges" : "") + ")" +
                    (painted ? ", painted by height and slope" : "") + ". Undo takes it back; saving writes it.");
    }
}
