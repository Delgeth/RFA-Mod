using System.Numerics;
using System.Windows;
using System.Windows.Controls;
using RfaFormats.Meshes;

namespace RfaEditor;

// The foliage painter: two of the sculptor's modes (Paint foliage, Erase foliage), with its brush.
// A zone's foliage (see ZoneFoliage) isn't a list of pieces: its terrain file marks which sub-cubes
// (1 m) of each 4 m cell each layer grows in, and the game scatters the layer's meshes over them. The
// painter marks (or clears) the chosen layer's sub-cubes under the brush where the ground is: the one
// a cell column's ground lies in, as the game sets each piece on the surface it finds in its
// sub-cube. It can only paint in cells where the file already has a patch for that layer (the cells
// and patches stay as they are, so the file keeps its size; growing them anew isn't known yet), and
// erase anywhere. The editor draws the foliage again after each stroke; saving writes the marks into
// the zone's terrain file (every copy), and the game grows it once the level is loaded again.
public partial class LevelEditor
{
    ZoneFoliage? paintFoliage;                                              // the zone's foliage as read (its layers, corner and patches)
    string? paintZone;                                                      // whose (the zone its terrain file is named after)
    Dictionary<(int X, int Y, int Z, int Layer), ulong>? foliageMasks;      // every patch's marks now
    Dictionary<(int X, int Y, int Z, int Layer), ulong>? foliageBefore;     // ...as a stroke began, for undoing it
    bool foliageStroke, foliageChanged;

    void OnSculptModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FoliageLayerBox == null) return;  // (while the view is being built)
        bool painting = ChosenMode is SculptMode.PaintFoliage or SculptMode.EraseFoliage;
        FoliageLayerBox.Visibility = painting ? Visibility.Visible : Visibility.Collapsed;
        if (painting) EnsureFoliagePaint();
        bool ground = ChosenMode is SculptMode.PaintGround or SculptMode.PaintRoad;
        GroundLayerBox.Visibility = LayerTextureButton.Visibility = ground ? Visibility.Visible : Visibility.Collapsed;
        if (ground) EnsureGroundPaint();
        bool chunks = ChosenMode is SculptMode.PaintChunks or SculptMode.EraseChunks;
        ChunkStyleBox.Visibility = chunks ? Visibility.Visible : Visibility.Collapsed;
        if (chunks) EnsureChunkStyles();
        ShowBrush();
    }

    /// <summary>Reads the zone's foliage for painting (once); false, said in the log, if it has none.</summary>
    bool EnsureFoliagePaint()
    {
        if (paintFoliage != null) return true;
        string zone = TerrainZone;
        if (context.Assets.ReadFile(zone + ".cterrain_pc", archive, container) is not { } file)
        {
            context.Log($"Foliage: {zone} has no terrain file, so no foliage to paint.");
            return false;
        }
        try
        {
            paintFoliage = ZoneFoliage.Read(file.Data, zone);
        }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or NotSupportedException)
        {
            context.Log($"Foliage: {zone}.cterrain_pc's foliage couldn't be read ({ex.Message}).");
            return false;
        }
        if (paintFoliage == null || paintFoliage.Layers.Count == 0)
        {
            paintFoliage = null;
            context.Log($"Foliage: {zone} has no foliage layers to paint (11 of the game's zones have foliage: world_08_02, the first mission's, and ten others).");
            return false;
        }
        paintZone = zone;
        foliageMasks = [];
        foreach (FoliagePatch patch in paintFoliage.Patches) foliageMasks[(patch.X, patch.Y, patch.Z, patch.Layer)] = patch.Mask;
        FoliageLayerBox.Items.Clear();
        for (int l = 0; l < paintFoliage.Layers.Count; l++)
        {
            var models = paintFoliage.Layers[l].Models;
            string first = models.Count > 0 ? System.IO.Path.GetFileNameWithoutExtension(models[0].Mesh) : "(no meshes)";
            int cells = paintFoliage.Patches.Count(p => p.Layer == l);
            FoliageLayerBox.Items.Add(new ComboBoxItem
            {
                Content = $"{l + 1}: {first}" + (models.Count > 1 ? $" +{models.Count - 1}" : ""),
                ToolTip = $"Layer {l + 1}: {string.Join(", ", models.Select(m => System.IO.Path.GetFileNameWithoutExtension(m.Mesh)))}. " +
                          $"It can grow in the {cells:N0} cells (4 m) where the zone has it already.",
            });
        }
        FoliageLayerBox.SelectedIndex = 0;
        context.Log($"Foliage: {zone}'s {paintFoliage.Layers.Count} foliage layers, in {paintFoliage.Patches.Count:N0} patches. Hold the left button to paint the chosen " +
                    "layer under the brush (Shift: erase). It grows only in cells where the zone has that layer already; saving writes it into the terrain file.");
        return true;
    }

    bool BeginFoliageStroke()
    {
        if (!EnsureFoliagePaint() || foliageMasks == null) return false;
        foliageBefore = new(foliageMasks);
        foliageStroke = true;
        return true;
    }

    /// <summary>A step of a foliage stroke: the chosen layer's sub-cubes under the brush marked where the ground is (or cleared).</summary>
    void PaintFoliage(Vector2 centre, float radius, bool erase)
    {
        if (paintFoliage == null || foliageMasks == null) return;
        int layer = Math.Clamp(FoliageLayerBox.SelectedIndex, 0, paintFoliage.Layers.Count - 1);
        float size = paintFoliage.Layers[layer].CellSize > 0 ? paintFoliage.Layers[layer].CellSize : 4, sub = size / 4;
        Vector3 corner = paintFoliage.Corner;
        foreach (var key in foliageMasks.Keys.Where(k => k.Layer == layer).ToList())
        {
            float cellX = corner.X + key.X * size, cellZ = corner.Z + key.Z * size;
            float nearestX = Math.Clamp(centre.X, cellX, cellX + size), nearestZ = Math.Clamp(centre.Y, cellZ, cellZ + size);
            if (Vector2.Distance(new Vector2(nearestX, nearestZ), centre) > radius) continue;
            ulong mask = foliageMasks[key], was = mask;
            for (int bit = 0; bit < 64; bit++)
            {
                int sx = (bit >> 4) & 3, sh = (bit >> 2) & 3, sz = bit & 3;
                float wx = cellX + (sx + 0.5f) * sub, wz = cellZ + (sz + 0.5f) * sub;
                if (Vector2.Distance(new Vector2(wx, wz), centre) > radius) continue;
                if (erase)
                {
                    mask &= ~(1UL << bit);
                    continue;
                }
                float bottom = key.Y * size + sh * sub, ground = GroundHeight(wx, wz);
                if (ground >= bottom - 0.05f && ground < bottom + sub) mask |= 1UL << bit;
            }
            if (mask == was) continue;
            foliageMasks[key] = mask;
        }
    }

    /// <summary>Ends a foliage stroke (if one was going on): keeps it to undo and draws the foliage again. True if it was one.</summary>
    bool EndFoliageStroke()
    {
        if (!foliageStroke) return false;
        foliageStroke = false;
        if (foliageMasks == null || foliageBefore is not { } before) return true;
        foliageBefore = null;
        var after = new Dictionary<(int X, int Y, int Z, int Layer), ulong>(foliageMasks);
        int cells = after.Count(kv => before.TryGetValue(kv.Key, out ulong then) && then != kv.Value);
        if (cells == 0)
        {
            context.Log("Foliage: nothing changed (the chosen layer only grows in cells where the zone has it already).");
            return true;
        }
        void Put(Dictionary<(int X, int Y, int Z, int Layer), ulong> masks)
        {
            foliageMasks = new(masks);
            foliageChanged = true;
            BuildFoliage();
        }
        foliageChanged = true;
        BuildFoliage();
        LiveNeedsReload();  // the game grows it once the level is loaded again
        Record(new Change(null, $"Foliage ({cells} cells)", Undo: () => Put(before), Redo: () => Put(after)));
        return true;
    }

    /// <summary>A zone's terrain file as the painter has its foliage now (the bytes given, if it isn't the one painted).</summary>
    byte[] FoliageAsPainted(string zone, byte[] data)
    {
        if (foliageMasks == null || paintZone == null || !GroundKey(zone).Equals(paintZone, StringComparison.OrdinalIgnoreCase)) return data;
        byte[] painted = (byte[])data.Clone();
        try { ZoneFoliage.WithMasks(painted, paintZone, foliageMasks); }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or NotSupportedException) { return data; }
        return painted;
    }

    /// <summary>Writes the painted foliage into the zone's terrain file (every copy), if it changed. Returns what was done, for the log.</summary>
    string SaveFoliage()
    {
        if (!foliageChanged || foliageMasks == null || paintZone == null) return "";
        string name = paintZone + ".cterrain_pc";
        if (context.Assets.ReadFile(name, archive, container) is not { } file) return $" Foliage: {name} couldn't be read, so it wasn't saved.";
        try
        {
            byte[] painted = (byte[])file.Data.Clone();
            int changed = ZoneFoliage.WithMasks(painted, paintZone, foliageMasks);
            foliageChanged = false;
            if (changed == 0) return "";
            return $" Foliage: {changed} patches into {PutTerrainFiles(new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase) { [name] = painted })}.";
        }
        catch (Exception ex) when (ex is System.IO.InvalidDataException or NotSupportedException)
        {
            return $" Foliage couldn't be saved: {ex.Message}.";
        }
    }
}
