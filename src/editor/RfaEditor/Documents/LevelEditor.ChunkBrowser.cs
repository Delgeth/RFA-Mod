using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RfaEditor;

// The Add list's chunk browser: the game's chunks sorted by type, category, setting and pack, gone
// into like folders (a group, then narrowing it down), with the way back above the list.
public partial class LevelEditor
{
    /// <summary>A folder row: a facet's groups (no value), or one group to go into.</summary>
    sealed record ChunkFolder(ChunkFacet Facet, string? Value);

    const int MostChunkRows = 400;

    /// <summary>Where the browser is: the groups chosen, in order; the last may be a facet whose groups are listed (no value yet).</summary>
    readonly List<(ChunkFacet Facet, string? Value)> chunkPath = [];

    string? ChosenPack => chunkPath.FirstOrDefault(p => p.Facet == ChunkFacet.Pack).Value;

    /// <summary>The facets a view can be narrowed by: a pack's parts only once a pack is chosen.</summary>
    IEnumerable<ChunkFacet> OpenFacets() =>
        new[] { ChunkFacet.Type, ChunkFacet.Category, ChunkFacet.Setting, ChunkFacet.Pack, ChunkFacet.Container }
            .Where(f => chunkPath.All(p => p.Facet != f) && (f != ChunkFacet.Container || ChosenPack != null));

    void FillChunkBrowser(string filter)
    {
        if (!context.Assets.IndexReady)
        {
            AddList.Items.Add(AddListNote("The game's chunks show here once the editor has read its files"));
            return;
        }
        IReadOnlyList<ChunkEntry> catalog = context.Assets.ChunkCatalog();
        if (catalog.Count == 0) return;
        string? pack = ChosenPack;
        var chosen = catalog.Where(e => chunkPath.All(p => p.Value == null || e.In(p.Facet, p.Value, pack))).ToList();

        if (chunkPath.Count == 0)
        {
            if (filter.Length >= 2)
            {
                var matches = chosen.Where(e => e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
                if (matches.Count > 0) AddList.Items.Add(AddListHeading($"ANY CHUNK FROM THE GAME ({matches.Count:N0})"));
                AddChunkRows(matches, ChunkFacet.Type);
            }
            else
            {
                AddList.Items.Add(AddListHeading($"BROWSE THE GAME'S {catalog.Count:N0} CHUNKS"));
                AddFacetRows(chosen);
                AddList.Items.Add(AddListNote("Or type a name to find any chunk"));
            }
            return;
        }

        (ChunkFacet facet, string? value) = chunkPath[^1];
        if (value == null)
        {
            // A facet's groups, each a folder, with how many chunks are in it.
            var groups = chosen.SelectMany(e => e.Groups(facet, pack)).GroupBy(g => g, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Name: g.Key, Count: g.Count()))
                .Where(g => filter.Length == 0 || g.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(g => g.Name is "Other" or "(loose in the pack)").ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase).ToList();
            AddList.Items.Add(AddListHeading($"CHUNKS BY {ChunkCatalog.Title(facet).ToUpperInvariant()} ({groups.Count})"));
            foreach ((string name, int count) in groups)
                AddList.Items.Add(AddListRow(ChunkKind, name, null, $"{count:N0}  ▸", $"The {count:N0} chunks of {ChunkCatalog.Title(facet)} {name}: go into them.",
                                             new ChunkFolder(facet, name)));
            return;
        }

        // A group: ways to narrow it down, then its chunks.
        var matching = filter.Length == 0 ? chosen : chosen.Where(e => e.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();
        if (filter.Length == 0 && chosen.Count > 12)
        {
            int before = AddList.Items.Count;
            AddList.Items.Add(AddListHeading("NARROW DOWN"));
            if (AddFacetRows(chosen) == 0) AddList.Items.RemoveAt(before);
        }
        AddList.Items.Add(AddListHeading($"{matching.Count:N0} CHUNK{(matching.Count == 1 ? "" : "S")}"));
        ChunkFacet by = new[] { ChunkFacet.Category, ChunkFacet.Type, ChunkFacet.Setting }.FirstOrDefault(f => chunkPath.All(p => p.Facet != f), ChunkFacet.Pack);
        AddChunkRows(matching, by);
    }

    static ObjectKind ChunkKind => ObjectKind.Of("rfg_mover", true);

    static ListBoxItem AddListNote(string text) => new()
    {
        IsEnabled = false, Focusable = false,
        Content = new TextBlock { Text = text, FontSize = 11, Foreground = Theme.Brush("DimTextBrush") },
    };

    /// <summary>A folder row for each facet still open that splits these chunks into two groups or more; how many were added.</summary>
    int AddFacetRows(List<ChunkEntry> chunks)
    {
        string? pack = ChosenPack;
        int added = 0;
        foreach (ChunkFacet facet in OpenFacets())
        {
            int groups = chunks.SelectMany(e => e.Groups(facet, pack)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            if (groups < 2) continue;
            string title = ChunkCatalog.Title(facet);
            AddList.Items.Add(AddListRow(ChunkKind, $"By {title}", null, $"{groups:N0} groups  ▸",
                                         $"These chunks sorted by {title}: pick a group to go into.", new ChunkFolder(facet, null)));
            added++;
        }
        return added;
    }

    /// <summary>The chunks' rows, under a small heading for each group of a facet (as many as the list takes).</summary>
    void AddChunkRows(List<ChunkEntry> chunks, ChunkFacet groupedBy)
    {
        string? pack = ChosenPack;
        int rows = 0;
        var groups = chunks.GroupBy(e => groupedBy is ChunkFacet.Pack or ChunkFacet.Container ? e.Groups(groupedBy, pack).FirstOrDefault() ?? "" : e.Of(groupedBy),
                                    StringComparer.OrdinalIgnoreCase)
                           .OrderBy(g => g.Key is "Other" or "").ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var group in groups)
        {
            if (rows >= MostChunkRows) break;
            if (groups.Count > 1)
                AddList.Items.Add(new ListBoxItem
                {
                    IsEnabled = false, Focusable = false,
                    Content = new TextBlock { Text = $"{group.Key}  ·  {group.Count():N0}", FontSize = 11, FontWeight = FontWeights.SemiBold,
                                              Foreground = Theme.Brush("DimTextBrush"), Margin = new Thickness(0, 4, 0, 0) },
                });
            foreach (ChunkEntry chunk in group.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (rows++ >= MostChunkRows) break;
                string packs = string.Join(", ", chunk.Places.Select(p => p.Container == null ? p.Pack : $"{p.Pack} › {p.Container}").Distinct().Take(6)) +
                               (chunk.Places.Count > 6 ? $" and {chunk.Places.Count - 6} more" : "");
                AddList.Items.Add(chunk.Stitch
                    ? AddListRow(ObjectKind.Geometry, chunk.Name, $"{chunk.Category} · {chunk.Setting}", chunk.Places.FirstOrDefault().Pack ?? "",
                                 $"{chunk.Name}: level geometry (a stitch piece), {chunk.Category.ToLowerInvariant()}, {chunk.Setting}.\nIn {packs}.\n" +
                                 "Added to the zone's stitch list where the middle of the view points; its model and textures go into the list's container when you save.",
                                 new GeometryChoice(chunk.Name))
                    : AddListRow(ChunkKind, chunk.Name, $"{chunk.Category} · {chunk.Setting}", chunk.Places.FirstOrDefault().Pack ?? "",
                                 $"{chunk.Name}: {chunk.Type.ToLowerInvariant()}, {chunk.Category.ToLowerInvariant()}, {chunk.Setting}.\nIn {packs}.\n" +
                                 "Added where the middle of the view points; its model and textures go into this zone's container when you save.",
                                 new ChunkChoice(chunk.Name)));
            }
        }
        if (chunks.Count > MostChunkRows)
            AddList.Items.Add(AddListNote($"{chunks.Count - MostChunkRows:N0} more: narrow down, or type more of a name"));
    }

    /// <summary>Goes into a folder: a facet's groups, or one of them.</summary>
    void OpenChunkFolder(ChunkFolder folder)
    {
        if (folder.Value != null && chunkPath.Count > 0 && chunkPath[^1] == (folder.Facet, null)) chunkPath[^1] = (folder.Facet, folder.Value);
        else chunkPath.Add((folder.Facet, folder.Value));
        RefillAddList();
    }

    /// <summary>Up one level: from a group to its facet's groups, from those to where the browser was before.</summary>
    void ChunkFolderUp()
    {
        if (chunkPath.Count == 0) return;
        (ChunkFacet facet, string? value) = chunkPath[^1];
        if (value != null) chunkPath[^1] = (facet, null);
        else chunkPath.RemoveAt(chunkPath.Count - 1);
        RefillAddList();
    }

    void RemoveFromChunkPath(int index)
    {
        if (index < 0 || index >= chunkPath.Count) return;
        bool pack = chunkPath[index].Facet == ChunkFacet.Pack;
        chunkPath.RemoveAt(index);
        if (pack) chunkPath.RemoveAll(p => p.Facet == ChunkFacet.Container);  // its parts go with it
        RefillAddList();
    }

    void OnAddBackClicked(object sender, RoutedEventArgs e) => ChunkFolderUp();

    /// <summary>Fills the list again for where the browser is now (the Find box emptied), with its first entry chosen.</summary>
    void RefillAddList()
    {
        if (AddFilterBox.Text.Length > 0) AddFilterBox.Text = "";  // (fills the list)
        else FillAddList("");
        StepAddList(1, fromStart: true);
        if (AddList.SelectedItem is ListBoxItem item) item.Focus();
    }

    /// <summary>Shows where the browser is above the list: Chunks › each group chosen (x takes it away).</summary>
    void ShowAddPath()
    {
        AddPathBar.Visibility = chunkPath.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AddPath.Children.Clear();
        if (chunkPath.Count == 0) return;
        var dim = Theme.Brush("DimTextBrush");
        AddPath.Children.Add(new TextBlock { Text = "Chunks", Foreground = dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        for (int i = 0; i < chunkPath.Count; i++)
        {
            (ChunkFacet facet, string? value) = chunkPath[i];
            AddPath.Children.Add(new TextBlock { Text = "›", Foreground = dim, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            if (value == null)
            {
                AddPath.Children.Add(new TextBlock { Text = $"by {ChunkCatalog.Title(facet)}", FontStyle = FontStyles.Italic, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
                continue;
            }
            int index = i;
            var chip = new Button
            {
                Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 1, 4, 1),
                Content = $"{value}  ✕",
                ToolTip = $"{ChunkCatalog.Title(facet)}: {value}. Click to take it away.",
            };
            chip.Click += (_, _) => RemoveFromChunkPath(index);
            AddPath.Children.Add(chip);
        }
    }
}
