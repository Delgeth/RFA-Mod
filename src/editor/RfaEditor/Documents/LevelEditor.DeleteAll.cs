using System.Windows;
using System.Windows.Controls;
using RfaFormats.Zones;

namespace RfaEditor;

// Delete all (in the Edit menu): every chunk (object placing a model), every piece of level geometry,
// or every object of the picked one's class, at once, as one step to undo. What other things name (a trigger's
// links, a navpoint path, an object in a mission layer or the persistent zone) is left, as Delete
// leaves it, and the log says how many.
public partial class LevelEditor
{
    static bool IsChunk(ZoneObject obj) => !IsGeometry(obj) && LevelScene.ModelOf(obj) is string model && model.EndsWith(".cchk_pc", StringComparison.OrdinalIgnoreCase);

    /// <summary>Edit > Delete all: its choices, each with how many it would delete, filled in as it opens.</summary>
    void OnDeleteAllMenuOpened(object sender, RoutedEventArgs e)
    {
        DeleteAllMenu.Items.Clear();
        void Item(string text, Func<ZoneObject, bool> which, string what)
        {
            int count = zone.Objects.Count(o => Editable(o) && which(o) && o.Parent == ZoneObject.None);
            var item = new MenuItem { Header = $"{text} ({count:N0})", IsEnabled = count > 0 };
            item.Click += (_, _) => DeleteAll(which, what);
            DeleteAllMenu.Items.Add(item);
        }
        Item("Chunks (buildings and props)", IsChunk, "chunk");
        Item("Level geometry (stitch pieces)", IsGeometry, "piece of level geometry");
        Item("Chunks and level geometry", o => IsChunk(o) || IsGeometry(o), "chunk or piece of level geometry");
        if (selected != null && Editable(selected))
        {
            uint type = selected.ClassHash;
            string name = names.Class(type);
            Item($"Every {name}", o => o.ClassHash == type, name);
        }
    }

    /// <summary>Deletes every editable object that <paramref name="which"/> takes (and what is attached to them), as one step, leaving those other things name.</summary>
    void DeleteAll(Func<ZoneObject, bool> which, string what)
    {
        CommitEdit();
        var roots = zone.Objects.Where(o => Editable(o) && which(o) && o.Parent == ZoneObject.None).ToList();
        if (roots.Count == 0) return;
        if (MessageBox.Show($"Delete every {what} in {zoneName} ({roots.Count:N0}, with what is attached to them)? It is one step to undo.",
                            "Delete all", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        var going = new List<ZoneObject>();
        var seen = new HashSet<ZoneObject>();
        foreach (ZoneObject root in roots)
            foreach (ZoneObject obj in zone.WithAttached(root))
                if (seen.Add(obj)) going.Add(obj);
        // Those something else names (outside what goes) stay, with what they are attached to.
        var kept = new HashSet<ZoneObject>();
        foreach (ZoneObject root in roots)
        {
            var group = zone.WithAttached(root);
            bool named = group.Any(o => zone.ReferencesTo(o, going).Any() || RelatedFiles().Any(f => ZoneFile.ReferencesIn(f.Zone, o.Handle).Any()));
            if (named) foreach (ZoneObject o in group) kept.Add(o);
        }
        if (kept.Count > 0)
        {
            going = going.Where(o => !kept.Contains(o)).ToList();
            // What the kept ones name may now point at something going: keep checking until it settles.
            bool again = true;
            while (again)
            {
                again = false;
                foreach (ZoneObject root in roots.Where(r => !kept.Contains(r)))
                {
                    var group = zone.WithAttached(root);
                    if (group.Any(o => zone.ReferencesTo(o, going).Any()))
                    {
                        foreach (ZoneObject o in group) kept.Add(o);
                        going = going.Where(o => !kept.Contains(o)).ToList();
                        again = true;
                    }
                }
            }
        }
        if (going.Count == 0)
        {
            MessageBox.Show($"Every {what} here is named by something else, so none was deleted.", "Delete all", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        int links = going.Sum(zone.DestructionLinksOf);
        RemovedObjects removed = zone.Remove(going);
        foreach (ZoneObject gone in going) scene?.Remove(gone);
        var all = going.ToList();
        Record(new Change(all[0], $"Delete all ({all.Count:N0})",
            Undo: () => { zone.PutBack(removed); foreach (ZoneObject gone in all) scene?.Add(gone); },
            Redo: () => { removed = zone.Remove(all); foreach (ZoneObject gone in all) scene?.Remove(gone); }, AddsOrRemoves: true));
        Select(null, focus: false);
        ObjectsChanged();
        int keptRoots = roots.Count(r => kept.Contains(r));
        context.Log($"Deleted all: {roots.Count - keptRoots:N0} {what}(s), {all.Count:N0} objects with what is attached to them" +
                    (links > 0 ? $", and their {links:N0} destruction link(s)" : "") +
                    (keptRoots > 0 ? $". {keptRoots:N0} were kept: something else names them (or what is attached to them)." : "."));
    }
}
